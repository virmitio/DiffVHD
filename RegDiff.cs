using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Spatial;
using System.Text;
using System.Text.RegularExpressions;
using ClrPlus.Core.Collections;
using DiscUtils.Registry;

namespace DiffVHD
{
    public class RegDiff
    {
        public class ValueObject
        {
            public RegistryValueType Type { get; private set; }
            public object Value { get; private set; }
            public ValueObject(RegistryValueType Kind, object Object)
            {
                Type = Kind;
                Value = Object;
            }
        }

        private readonly XDictionary<string, XDictionary<string, ValueObject>> Data;

        public RegDiff()
        {
            Data = new XDictionary<string, XDictionary<string, ValueObject>>();
        }

        public RegDiff(RegistryComparison Source, RegistryComparison.Side Side)
            : this()
        {
            var origin = Source.Output;

            foreach (var data in origin)
            {
                int tmpIndex = data.Key.IndexOf("::");
                string path = data.Key.Substring(0, tmpIndex);
                string name = data.Key.Substring(tmpIndex + 2);

                if (!data.Value.Same)
                    if (Side == RegistryComparison.Side.A &&
                        !(data.Value.TypeA == RegistryValueType.None && data.Value.ValueA == null))
                        Data[path][name] = new ValueObject(data.Value.TypeA, data.Value.ValueA);
                    else if (!(data.Value.TypeB == RegistryValueType.None && data.Value.ValueB == null)) // Side == B
                        Data[path][name] = new ValueObject(data.Value.TypeB, data.Value.ValueB);
            }

        }

        public static RegDiff ReadFromStream(Stream Input)
        {
            try { return ReadFromHive(new RegistryHive(Input, DiscUtils.Ownership.None)); }
            catch (Exception e) { return new RegDiff(); }
        }

        public static RegDiff ReadFromHive(RegistryHive Input)
        {
            var Out = new RegDiff();
            var Root = Input.Root;
            foreach (var name in Root.GetValueNames())
            {
                Out.Data[String.Empty][name] = new ValueObject(Root.GetValueType(name), Root.GetValue(name));
            }
            foreach (var sub in Root.GetSubKeyNames())
            {
                InnerRead(Root.OpenSubKey(sub), Out.Data);
            }
            return Out;
        }

        private static void InnerRead(RegistryKey key, XDictionary<string, XDictionary<string, ValueObject>> data)
        {
            foreach (var name in key.GetValueNames())
            {
                data[key.Name][name] = new ValueObject(key.GetValueType(name), key.GetValue(name));
            }
            foreach (var sub in key.GetSubKeyNames())
            {
                InnerRead(key.OpenSubKey(sub), data);
            }
        }

        public void WriteToStream(Stream Output)
        {
            RegistryHive Out;

            try { Out = new RegistryHive(Output, DiscUtils.Ownership.None); }
            catch (Exception e) { Out = RegistryHive.Create(Output, DiscUtils.Ownership.None); }

            var root = Out.Root;

            foreach (var path in Data)
            {
                var currentKey = root.CreateSubKey(path.Key);
                foreach (var val in path.Value)
                    currentKey.SetValue(val.Key, val.Value.Value, val.Value.Type);
            }
        }

        public bool ApplyTo(RegistryKey Root, Action<string> Log = null)
        {
            if (Root == null)
                throw new ArgumentNullException("Root");
            bool status = true;
            try
            {
                foreach (var path in Data)
                {
                    try
                    {
                        RegistryKey currentPath = path.Key.Split('\\')
                                                      .Aggregate(Root, (current, sub) => current.CreateSubKey(sub));
                        foreach (var item in path.Value)
                        {
                            try
                            {
                                currentPath.SetValue(item.Key, item.Value.Value, item.Value.Type);
                            }
                            catch (Exception e)
                            {
                                if (Log != null)
                                    Log(e.Message + '\n' + e.StackTrace);
                                status = false;
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        if (Log != null)
                            Log(e.Message + '\n' + e.StackTrace);
                        status = false;
                    }
                }

            }
            catch (Exception e)
            {
                if (Log != null)
                    Log(e.Message + '\n' + e.StackTrace);
                status = false;
            }
            return status;
        }

    }
}
