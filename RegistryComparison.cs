using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiscUtils.Registry;

namespace DiffVHD
{
    public class RegistryComparison
    {
        public enum Side { A, B }

        public RegistryHive HiveA { get; private set; }
        public RegistryHive HiveB { get; private set; }
        public Dictionary<string, Data> Output { get; private set; }

        private bool HavePreference = false;
        private Side Preference;

        public RegistryComparison(string HiveFileA, string HiveFileB)
            : this(File.OpenRead(HiveFileA), File.OpenRead(HiveFileB))
        {}

        public RegistryComparison(Stream HiveFileA, Stream HiveFileB)
        {
            Output = new Dictionary<string, Data>();
            HiveA = new RegistryHive(HiveFileA);
            HiveB = new RegistryHive(HiveFileB);
        }

        public RegistryComparison(string HiveFileA, string HiveFileB, Side PreferSide)
            : this(File.OpenRead(HiveFileA), File.OpenRead(HiveFileB), PreferSide)
        {}

        public RegistryComparison(Stream HiveFileA, Stream HiveFileB, Side PreferSide)
            : this(HiveFileA, HiveFileB)
        {
            HavePreference = true;
            Preference = PreferSide;
        }
        

        public class Data
        {
            public RegistryValueType TypeA { get; private set; }
            public object ValueA { get; private set; }
            public RegistryValueType TypeB { get; private set; }
            public object ValueB { get; private set; }
            public bool Same { get; private set; }

            public Data()
            {
                TypeA = RegistryValueType.None;
                TypeB = RegistryValueType.None;
                ValueA = null;
                ValueB = null;
                Same = false;
            }

            public Data(object aVal, RegistryValueType aType, object bVal, RegistryValueType bType)
            {
                TypeA = aType;
                TypeB = bType;
                ValueA = aVal;
                ValueB = bVal;
                CheckSame();
            }

            public void SetA(object aVal, RegistryValueType aType)
            {
                TypeA = aType;
                ValueA = aVal;
                CheckSame();
            }

            public void SetB(object bVal, RegistryValueType bType)
            {
                TypeA = bType;
                ValueA = bVal;
                CheckSame();
            }

            public void CheckSame()
            {
                Same = ValueA != null && ValueB != null && (TypeA != TypeB && ValueA.Equals(ValueB));
            }
        }
        
        public bool DoCompare()
        {
            var t = HavePreference
                        ? Preference == Side.A
                              ? AOnlyInnerCompare(HiveA.Root, HiveB.Root, @"\")
                              : BOnlyInnerCompare(HiveA.Root, HiveB.Root, @"\")
                        : InnerCompare(HiveA.Root, HiveB.Root, @"\");
            t.Wait();
            return t.Result;
        }

        private Task<bool> InnerCompare(RegistryKey A, RegistryKey B, string root) // TODO:  Adjust to match the BOnly comparison (below)
        {

            List<Task<bool>> tasks = new List<Task<bool>>();
            try
            {
                if (A != null)
                {
                    // Process A
                    string[] aVals;
                    lock (HiveA)
                        aVals = A.GetValueNames();
                    foreach (var Name in aVals)
                    {
                        string EntryName;
                        lock (HiveA)
                            EntryName = root + A.Name + "::" + Name;
                        var dat = new Data();
                        lock (HiveA)
                            dat.SetA(A.GetValue(Name), A.GetValueType(Name));
                        Output.Add(EntryName, dat);
                    }
                    string[] ASubKeys;
                    lock (HiveA)
                        ASubKeys = A.GetSubKeyNames();
                    string[] BSubKeys = new string[0];
                    if (B != null)
                        lock (HiveB)
                            BSubKeys = B.GetSubKeyNames();
                    tasks.AddRange(ASubKeys.AsParallel().Select(keyName =>
                        {
                            RegistryKey aSub, bSub;
                            lock (HiveA)
                                aSub = A.OpenSubKey(keyName);
                            lock (HiveB)
                                bSub = B == null
                                           ? null
                                           : BSubKeys.Contains(keyName, StringComparer.CurrentCultureIgnoreCase)
                                                 ? B.OpenSubKey(keyName)
                                                 : null;
                            return InnerCompare(aSub, bSub, root + keyName + @"\");
                        }));
                }
                if (B != null)
                {
                    // Process B
                    string[] bVals;
                    lock (HiveB)
                        bVals = B.GetValueNames();

                    foreach (var Name in bVals)
                    {
                        string EntryName;
                        lock (HiveB)
                            EntryName = root + B.Name + "::" + Name;
                        Data dat = Output.ContainsKey(EntryName) ? Output[EntryName] : new Data();
                        lock (HiveB)
                            dat.SetB(B.GetValue(Name), B.GetValueType(Name));
                        Output[EntryName] = dat;
                    }
                    string[] BSubKeys;
                    lock (HiveB)
                        BSubKeys = B.GetSubKeyNames();
                    tasks.AddRange(BSubKeys.AsParallel().Select(keyName =>
                        {
                            RegistryKey bSub;
                            lock (HiveB)
                                bSub = B.OpenSubKey(keyName);
                            return InnerCompare(null, bSub, root + keyName + @"\");
                        }));
                }

                return Task.Factory.StartNew(() =>
                                             tasks.Aggregate(true, (ret, task) =>
                                                 {
                                                     task.Wait();
                                                     return ret && task.Result;
                                                 }), TaskCreationOptions.AttachedToParent);
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private Task<bool> AOnlyInnerCompare(RegistryKey A, RegistryKey B, string root)  // TODO:  Adjust to match the BOnly comparison (below)
        {

            List<Task<bool>> tasks = new List<Task<bool>>();
            try
            {
                if (A != null)
                {
                    // Process A
                    string[] aVals;
                    lock (HiveA)
                        aVals = A.GetValueNames();
                    string[] bVals = new string[0];
                    if (B != null)
                        lock (HiveB)
                            bVals = B.GetValueNames();
                    foreach (var Name in aVals)
                    {
                        string EntryName;
                        lock (HiveA)
                            EntryName = root + A.Name + "::" + Name;
                        var dat = new Data();
                        lock (HiveA)
                            dat.SetA(A.GetValue(Name), A.GetValueType(Name));
                        if (bVals.Contains(Name, StringComparer.CurrentCultureIgnoreCase))
                            lock (HiveB)
                                dat.SetB(B.GetValue(Name), B.GetValueType(Name));
                        Output.Add(EntryName, dat);
                    }
                    string[] ASubKeys;
                    lock (HiveA)
                        ASubKeys = A.GetSubKeyNames();
                    string[] BSubKeys = new string[0];
                    if (B != null)
                        lock (HiveB)
                            BSubKeys = B.GetSubKeyNames();
                    tasks.AddRange(ASubKeys.AsParallel().Select(keyName =>
                    {
                        RegistryKey aSub, bSub;
                        lock (HiveA)
                            aSub = A.OpenSubKey(keyName);
                        lock (HiveB)
                            bSub = B == null
                                       ? null
                                       : BSubKeys.Contains(keyName, StringComparer.CurrentCultureIgnoreCase)
                                             ? B.OpenSubKey(keyName)
                                             : null;
                        return AOnlyInnerCompare(aSub, bSub, root + keyName + @"\");
                    }));
                }

                return Task.Factory.StartNew(() =>
                                             tasks.Aggregate(true, (ret, task) =>
                                             {
                                                 task.Wait();
                                                 return ret && task.Result;
                                             }), TaskCreationOptions.AttachedToParent);
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private async Task<bool> BOnlyInnerCompare(RegistryKey A, RegistryKey B, string root)
        {

            List<Task<bool>> tasks = new List<Task<bool>>();
            List<bool> bools = new List<bool>();
            try
            {
                if (B != null)
                {
                    // Process A
                    string[] bVals;
                    lock (HiveB)
                        bVals = B.GetValueNames();
                    string[] aVals = new string[0];
                    if (A != null)
                        lock (HiveA)
                            aVals = A.GetValueNames();
                    foreach (var Name in bVals)
                    {
                        string EntryName;
                        lock (HiveB)
                            EntryName = root + B.Name + "::" + Name;
                        var dat = new Data();
                        lock (HiveB)
                            dat.SetB(B.GetValue(Name), B.GetValueType(Name));
                        if (aVals.Contains(Name, StringComparer.CurrentCultureIgnoreCase))
                            lock (HiveA)
                                dat.SetA(A.GetValue(Name), A.GetValueType(Name));
                        lock(Output)
                            Output.Add(EntryName, dat);
                    }
                    string[] BSubKeys;
                    lock (HiveB)
                        BSubKeys = B.GetSubKeyNames();
                    string[] ASubKeys = new string[0];
                    if (A != null)
                        lock (HiveA)
                            ASubKeys = A.GetSubKeyNames();
                    tasks.AddRange(BSubKeys.Select(async keyName => 
                    {
                        RegistryKey aSub, bSub;
                        lock (HiveB)
                            bSub = B.OpenSubKey(keyName);
                        lock (HiveA)
                            aSub = A == null
                                       ? null
                                       : ASubKeys.Contains(keyName, StringComparer.CurrentCultureIgnoreCase)
                                             ? A.OpenSubKey(keyName)
                                             : null;
                        return await BOnlyInnerCompare(aSub, bSub, root + keyName + @"\");
                    }));
                }

                /*
                return Task.Factory.StartNew(() =>
                                             tasks.AsParallel().Aggregate(true, (ret, task) =>
                                             {
                                                 task.Wait();
                                                 return ret && task.Result;
                                             }), TaskCreationOptions.AttachedToParent);
                */
                return tasks.AsParallel().Aggregate(true, (ret, task) =>
                                             {
                                                 task.Wait();
                                                 return ret && task.Result;
                                             });
            }
            catch (Exception e)
            {
                throw;
            }
        }

    }
}
