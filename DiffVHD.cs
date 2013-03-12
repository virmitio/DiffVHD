using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClrPlus.Core.Collections;
using ClrPlus.Core.Exceptions;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Registry;
using GitSharpImport;
using GitSharpImport.Core.Diff;
using GitSharpImport.Core.Patch;
using GitSharpImport.Core.Util;

namespace DiffVHD
{
    public static class DiffVHD
    {
        public enum DiskType
        {
            VHD,
            VHDX
        }

        private const string RootFiles = "FILES";
        private const string RootSystemRegistry = @"REGISTRY\\SYSTEM";
        private const string RootUserRegistry = @"REGISTRY\\USERS";

        private static readonly string[] ExcludeFiles = new string[] { @"PAGEFILE.SYS", @"HIBERFIL.SYS", @"SYSTEM VOLUME INFORMATION\"};//, @"WINDOWS\SYSTEM32\CONFIG" };

        private static readonly Regex[] ExclusionRules = new Regex[]
            {
                new Regex(@"^WIN[^\\]*\\SYSTEM32\\CONFIG\\(^DEFAULT&^SOFTWARE&^SYSTEM&^SYSTEMPROFILE\\NTUSER.DAT)$", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase)
            };

        private static readonly string[] SystemRegistryFiles = new string[]
            {
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\BCD-TEMPLATE",RootFiles),
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\COMPONENTS",RootFiles),
                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\DEFAULT",RootFiles),
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\DRIVERS",RootFiles),
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\FP",RootFiles),
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\SAM",RootFiles),
//                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\SECURITY",RootFiles),
                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\SOFTWARE",RootFiles),
                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\SYSTEM",RootFiles),
                String.Format(@"{0}\WINDOWS\SYSTEM32\CONFIG\SYSTEMPROFILE\NTUSER.DAT",RootFiles),
            };

        private static readonly Regex UserRegisrtyFiles = new Regex(@"^.*\\?(?<parentDir>Documents and Settings|Users)\\(?<user>[^\\]+)\\ntuser.dat$", RegexOptions.IgnoreCase);
        private static Regex GetUserRegex(string Username) { return new Regex(@"^.*\\?(?<parentDir>Documents and Settings|Users)\\" + Username + @"\\ntuser.dat$", RegexOptions.IgnoreCase); }
        private static readonly Regex DiffUserRegistry = new Regex(@"^\\?" + RootUserRegistry + @"\\(?<user>[^\\]+)\\ntuser.dat$", RegexOptions.IgnoreCase);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="OldVHD"></param>
        /// <param name="NewVHD"></param>
        /// <param name="Output">Filename to the output file.  Method will fail if this already exists unless Force is passed as 'true'.</param>
        /// <param name="OutputType">A <see cref="DiffVHD.DiskType"/> which specifies the output file format.</param>
        /// <param name="Force">If true, will overwrite the Output file if it already exists.  Defaults to 'false'.</param>
        /// <param name="Partition">The 0-indexed partition number to compare from each disk file.</param>
        /// <param name="Style"></param>
        /// <returns></returns>
        public static void CreateDiff(string OldVHD, string NewVHD, string Output, int? Partition, DiskType OutputType = DiskType.VHD, bool Force = false, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            CreateDiff(OldVHD, NewVHD, Output, OutputType, Force, Partition.HasValue ? new Tuple<int, int>(Partition.Value, Partition.Value) : null, Style: Style);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="OldVHD"></param>
        /// <param name="NewVHD"></param>
        /// <param name="Output">Filename to the output file.  Method will fail if this already exists unless Force is passed as 'true'.</param>
        /// <param name="OutputType">A <see cref="DiffVHD.DiskType"/> which specifies the output file format.</param>
        /// <param name="Force">If true, will overwrite the Output file if it already exists.  Defaults to 'false'.</param>
        /// <param name="Partition">An int tuple which declares a specific pair of partitions to compare.  The first value in the tuple will be the 0-indexed partition number from OldVHD to compare against.  The second value of the tuple will be the 0-indexed parition from NewVHD to compare with.</param>
        /// <param name="Style"></param>
        /// <returns></returns>
        public static void CreateDiff(string OldVHD, string NewVHD, string Output, DiskType OutputType = DiskType.VHD, bool Force = false, Tuple<int, int> Partition = null, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            if (File.Exists(Output) && !Force) throw new ArgumentException("Output file already exists.", "Output");
            if (!File.Exists(OldVHD)) throw new ArgumentException("Input file does not exist.", "OldVHD");
            if (!File.Exists(NewVHD)) throw new ArgumentException("Input file does not exist.", "NewVHD");

            // byte[] CopyBuffer = new byte[1024*1024];
            VirtualDisk Old, New, Out;
            Old = VirtualDisk.OpenDisk(OldVHD, FileAccess.Read);
            New = VirtualDisk.OpenDisk(NewVHD, FileAccess.Read);

            using (Old)
            using (New)
            using (var OutFS = new FileStream(Output, Force ? FileMode.Create : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {

                // Check type of filesystems being compared
                if (!Old.IsPartitioned) throw new ArgumentException("Input disk is not partitioned.", "OldVHD");
                if (!New.IsPartitioned) throw new ArgumentException("Input disk is not partitioned.", "NewVHD");

                long CapacityBuffer = 64 * Math.Max(Old.Geometry.BytesPerSector, New.Geometry.BytesPerSector); // starting with 64 sectors as a buffer for partition information in the output file
                long[] OutputCapacities = new long[Partition != null ? 1 : Old.Partitions.Count];

                if (Partition != null)
                {
                    var PartA = Old.Partitions[Partition.Item1];
                    var PartB = New.Partitions[Partition.Item2];
                    if (PartA.BiosType != PartB.BiosType)
                        throw new InvalidFileSystemException(
                            String.Format(
                                "Filesystem of partition {0} in '{1}' does not match filesystem type of partition {2} in '{3}'.",
                                Partition.Item2, NewVHD, Partition.Item1, OldVHD));
                    OutputCapacities[0] += Math.Max(PartA.SectorCount * Old.Geometry.BytesPerSector, PartB.SectorCount * New.Geometry.BytesPerSector);
                }
                else
                {
                    if (Old.Partitions.Count != New.Partitions.Count)
                        throw new ArgumentException(
                            "Input disks do not have the same number of partitions.  To compare specific partitions on mismatched disks, provide the 'Partition' parameter.");
                    for (int i = 0; i < Old.Partitions.Count; i++)
                        if (Old.Partitions[i].BiosType != New.Partitions[i].BiosType)
                            throw new InvalidFileSystemException(String.Format("Filesystem of partition {0} in '{1}' does not match filesystem type of partition {0} in '{2}'.", i, NewVHD, OldVHD));
                        else
                            OutputCapacities[i] = Math.Max(Old.Partitions[i].SectorCount * Old.Geometry.BytesPerSector, New.Partitions[i].SectorCount * New.Geometry.BytesPerSector);
                }

                long OutputCapacity = CapacityBuffer + OutputCapacities.Sum();
                switch (OutputType)
                {
                    case DiskType.VHD:
                        Out = DiscUtils.Vhd.Disk.InitializeDynamic(OutFS, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 512 * 1024)); // the Max() is present only because there's currently a bug with blocksize < (8*sectorSize) in DiscUtils
                        break;
                    case DiskType.VHDX:
                        Out = DiscUtils.Vhdx.Disk.InitializeDynamic(OutFS, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 512 * 1024));
                        break;
                    default:
                        throw new NotSupportedException("The selected disk type is not supported at this time.",
                                                        new ArgumentException(
                                                            "Selected DiskType not currently supported.", "OutputType"));
                }

                using (Out)
                {

                    // set up the output location
                    if (Out is DiscUtils.Vhd.Disk) ((DiscUtils.Vhd.Disk) Out).AutoCommitFooter = false;
                    var OutParts = BiosPartitionTable.Initialize(Out);
                    
                    if (Partition != null)
                    {
                        OutParts.Create(GetPartitionType(Old.Partitions[Partition.Item1]), false); // there is no need (ever) for a VHD diff to have bootable partitions
                        var OutFileSystem = Out.FormatPartition(0);
                        DiffPart(DetectFileSystem(Old.Partitions[Partition.Item1]),
                                 DetectFileSystem(New.Partitions[Partition.Item2]),
                                 OutFileSystem,  // As we made the partition spen the entire drive, it should be the only partition
                                 Style);
                    }
                    else // Partition == null
                    {
                        for (int i = 0; i < Old.Partitions.Count; i++)
                        {
                            var partIndex = OutParts.Create(Math.Max(Old.Partitions[i].SectorCount * Old.Parameters.BiosGeometry.BytesPerSector, 
                                                                     New.Partitions[i].SectorCount * New.Parameters.BiosGeometry.BytesPerSector), 
                                                            GetPartitionType(Old.Partitions[i]), false);
                            var OutFileSystem = Out.FormatPartition(partIndex);
                            
                            DiffPart(DetectFileSystem(Old.Partitions[i]),
                                     DetectFileSystem(New.Partitions[i]),
                                     OutFileSystem,
                                     Style);
                        }
                    }
                    
                } // using (Out)

            } // using (Old, New, and OutFS)
        }

        private static WellKnownPartitionType GetPartitionType(PartitionInfo Partition)
        {
            switch (Partition.BiosType)
            {
                case BiosPartitionTypes.Fat16:
                case BiosPartitionTypes.Fat32:
                case BiosPartitionTypes.Fat32Lba:
                    return WellKnownPartitionType.WindowsFat;
                case BiosPartitionTypes.Ntfs:
                    return WellKnownPartitionType.WindowsNtfs;
                case BiosPartitionTypes.LinuxNative:
                    return WellKnownPartitionType.Linux;
                case BiosPartitionTypes.LinuxSwap:
                    return WellKnownPartitionType.LinuxSwap;
                case BiosPartitionTypes.LinuxLvm:
                    return WellKnownPartitionType.LinuxLvm;
                default:
                    throw new ArgumentException(
                        String.Format("Unsupported partition type: '{0}'", BiosPartitionTypes.ToString(Partition.BiosType)), "Partition");
            }
        }

        private static DiscFileSystem DetectFileSystem(PartitionInfo Partition)
        {
            using (var stream = Partition.Open())
            {
                if (NtfsFileSystem.Detect(stream))
                    return new NtfsFileSystem(Partition.Open());
                stream.Seek(0, SeekOrigin.Begin);
                if (FatFileSystem.Detect(stream))
                    return new FatFileSystem(Partition.Open());

                /* Ext2/3/4 file system - when Ext becomes fully writable
                
                stream.Seek(0, SeekOrigin.Begin);
                if (ExtFileSystem.Detect(stream))
                    return new ExtFileSystem(Partition.Open());
                */

                return null;
            }
        }

        private static void DiffPart(DiscFileSystem PartA, DiscFileSystem PartB, DiscFileSystem Output, ComparisonStyle Style = ComparisonStyle.DateTimeOnly, CopyQueue WriteQueue = null)
        {
            if (PartA == null) throw new ArgumentNullException("PartA");
            if (PartB == null) throw new ArgumentNullException("PartB");
            if (Output == null) throw new ArgumentNullException("Output");

            if (PartA is NtfsFileSystem)
            {
                ((NtfsFileSystem) PartA).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem) PartA).NtfsOptions.HideSystemFiles = false;
            }
            if (PartB is NtfsFileSystem)
            {
                ((NtfsFileSystem) PartB).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem) PartB).NtfsOptions.HideSystemFiles = false;
            }
            if (Output is NtfsFileSystem)
            {
                ((NtfsFileSystem) Output).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem) Output).NtfsOptions.HideSystemFiles = false;
            }

            if (WriteQueue == null) WriteQueue = new CopyQueue();

            var RootA = PartA.Root;
            var RootB = PartB.Root;
            var OutRoot = Output.Root;
            var OutFileRoot = Output.GetDirectoryInfo(RootFiles);
            if (!OutFileRoot.Exists) OutFileRoot.Create();

            CompareTree(RootA, RootB, OutFileRoot, WriteQueue, Style);

            WriteQueue.Go();

            // Now handle registry files (if any)
            ParallelQuery<DiscFileInfo> Ofiles;
            lock(OutFileRoot.FileSystem)
                Ofiles = OutFileRoot.GetFiles("*.*", SearchOption.AllDirectories).AsParallel();
            Ofiles = Ofiles.Where(dfi =>
                                    SystemRegistryFiles.Contains(dfi.FullName, StringComparer.CurrentCultureIgnoreCase));

            foreach (var file in Ofiles)
            {
                var A = PartA.GetFileInfo(file.FullName.Substring(RootFiles.Length + 1));
                if (!A.Exists)
                {
                    file.FileSystem.MoveFile(file.FullName, String.Concat(RootSystemRegistry, A.FullName));
                    continue;
                }
                //else
                MemoryStream SideA = new MemoryStream();
                using (var tmp = A.OpenRead()) tmp.CopyTo(SideA);
                MemoryStream SideB = new MemoryStream();
                using (var tmp = file.OpenRead()) tmp.CopyTo(SideB);
                var comp = new RegistryComparison(SideA, SideB, RegistryComparison.Side.B);
                comp.DoCompare();
                var diff = new RegDiff(comp, RegistryComparison.Side.B);
                var outFile = Output.GetFileInfo(Path.Combine(RootSystemRegistry, file.FullName));
                if (!outFile.Directory.Exists)
                {
                    outFile.Directory.Create();
                }
                using (var OUT = outFile.Open(outFile.Exists ? FileMode.Truncate : FileMode.CreateNew, FileAccess.ReadWrite))
                    diff.WriteToStream(OUT);
                file.Delete(); // remove this file from the set of file to copy and overwrite
            }

            lock (OutFileRoot.FileSystem)
                Ofiles = OutFileRoot.GetFiles("*.*", SearchOption.AllDirectories).AsParallel();
            Ofiles = Ofiles.Where(dfi => UserRegisrtyFiles.IsMatch(dfi.FullName));
                
            foreach (var file in Ofiles)
            {
                var match = UserRegisrtyFiles.Match(file.FullName);
                var A = PartA.GetFileInfo(file.FullName.Substring(RootFiles.Length + 1));
                if (!A.Exists)
                {
                    file.FileSystem.MoveFile(file.FullName,
                                                Path.Combine(RootUserRegistry, match.Groups["user"].Value, A.Name));
                    continue;
                }
                //else
                MemoryStream SideA = new MemoryStream();
                using (var tmp = A.OpenRead()) tmp.CopyTo(SideA);
                MemoryStream SideB = new MemoryStream();
                using (var tmp = file.OpenRead()) tmp.CopyTo(SideB);
                var comp = new RegistryComparison(SideA, SideB, RegistryComparison.Side.B);
                comp.DoCompare();
                var diff = new RegDiff(comp, RegistryComparison.Side.B);
                var outFile =
                    Output.GetFileInfo(Path.Combine(RootUserRegistry, match.Groups["user"].Value, file.FullName));
                if (!outFile.Directory.Exists)
                {
                    outFile.Directory.Create();
                }
                using (var OUT = outFile.Open(outFile.Exists ? FileMode.Truncate : FileMode.CreateNew, FileAccess.ReadWrite))
                    diff.WriteToStream(OUT);
                file.Delete(); // remove this file from the set of file to copy and overwrite
            }
        }

        public enum ComparisonStyle
        {
            /// <summary> For each pair of files with same name, perform DateTime compare.  If identical, continue with size and Binary compare. </summary>
            Full,
            /// <summary> Only compare filenames and sizes.  If a file exists on both sides with same size, assume identical. </summary>
            NameOnly,
            /// <summary> For each pair of files with same name, compare only DateTime and size. Does not compare content. </summary>
            DateTimeOnly,
            /// <summary> For each pair of files with same name, compares size and binary content regardless of DateTime. </summary>
            BinaryOnly,
            /// <summary> For each pair of files, compares the NTFS journal sequence numbers.  NTFS only. </summary>
            Journaled,
        }
        
        private static void CompareTree(DiscDirectoryInfo A, DiscDirectoryInfo B, DiscDirectoryInfo Out,
                                        CopyQueue WriteQueue, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            CompTree(A, B, Out, WriteQueue, Style).Wait();
        }


        private static Task CompTree(DiscDirectoryInfo A, DiscDirectoryInfo B, DiscDirectoryInfo Out,
                                        CopyQueue WriteQueue, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        
        {
            if (WriteQueue == null) throw new ArgumentNullException("WriteQueue");
            List<Task> tasks = new List<Task>();

            DiscFileSystem Alock = A.FileSystem;
            DiscFileSystem Block = B.FileSystem;
            DiscFileSystem Olock = Out.FileSystem;

            ParallelQuery<DiscFileInfo> BFiles;
            lock(Block)
                BFiles = B.GetFiles("*.*", SearchOption.AllDirectories).ToArray().AsParallel();
            BFiles = BFiles.Where(file =>
                                  !ExcludeFiles.Contains(file.FullName.ToUpperInvariant())
                                 ).AsParallel();
            BFiles = ExclusionRules.Aggregate(BFiles, (current, rule) => current.Where(file => !rule.IsMatch(file.FullName)));

                BFiles = BFiles.Where(file =>
                    {
                        DiscFileInfo Atmp;
                        lock (Alock)
                            Atmp = Alock.GetFileInfo(file.FullName);
                        return !CompareFile(Atmp, file, Style);
                    }).ToArray().AsParallel();
           
            foreach (var file in BFiles)
            {
                DiscFileInfo outFile;
                lock (Olock)
                    outFile = Out.FileSystem.GetFileInfo(Path.Combine(Out.FullName, file.FullName));
                lock (Alock)
                WriteQueue.Add(file, outFile, Alock.GetFileInfo(file.FullName));
            }

            return Task.Factory.StartNew(() => Task.WaitAll(tasks.ToArray()), TaskCreationOptions.AttachedToParent);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="A"></param>
        /// <param name="B"></param>
        /// <param name="Style"></param>
        /// <returns>True if A and B are equivalent.</returns>
        private static bool CompareFile(DiscFileInfo A, DiscFileInfo B, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            if (A == null || B == null) return A == B;
            lock (A.FileSystem)
                lock (B.FileSystem)
                    if (!A.Exists || !B.Exists) return false;

            if (Style == ComparisonStyle.Journaled)
                if (A.FileSystem is NtfsFileSystem)
                {
                    lock(A.FileSystem)
                        lock (B.FileSystem)
                        {
                            var An = A.FileSystem as NtfsFileSystem;
                            var Bn = (NtfsFileSystem) (B.FileSystem);
                            long Aid = (long) (An.GetFileId(A.FullName) & 0x0000ffffffffffff);
                            long Bid = (long) (Bn.GetFileId(B.FullName) & 0x0000ffffffffffff);

                            return An.GetMasterFileTable()[Aid].LogFileSequenceNumber ==
                                   Bn.GetMasterFileTable()[Bid].LogFileSequenceNumber;
                        }
                }
                else throw new ArgumentException("Journal comparison only functions on NTFS partitions.", "Style");

            bool LenCheck, WriteCheck;
            lock (A.FileSystem)
                lock (B.FileSystem)
                {
                    LenCheck = A.Length == B.Length;
                    WriteCheck = A.LastWriteTimeUtc == B.LastWriteTimeUtc;
                }

            return LenCheck &&
                   (Style == ComparisonStyle.NameOnly || (Style == ComparisonStyle.BinaryOnly
                                                              ? FilesMatch(A, B)
                                                              : (WriteCheck &&
                                                                 (Style == ComparisonStyle.DateTimeOnly ||
                                                                  FilesMatch(A, B)))));
        }

        private static bool FilesMatch(DiscFileInfo A, DiscFileInfo B)
        {
            const int BufferSize = 2048;  // arbitrarily chosen buffer size
            byte[] buffA = new byte[BufferSize];
            byte[] buffB = new byte[BufferSize];

            lock(A.FileSystem)
                lock (B.FileSystem)
                {
                    var fileA = A.OpenRead();
                    var fileB = B.OpenRead();

                    int numA, numB;
                    while (fileA.Position < fileA.Length)
                    {
                        numA = fileA.Read(buffA, 0, BufferSize);
                        numB = fileB.Read(buffB, 0, BufferSize);
                        if (numA != numB)
                        {
                            fileA.Close();
                            fileB.Close();
                            return false;
                        }
                        for (int i = 0; i < numA; i++)
                            if (buffA[i] != buffB[i])
                            {
                                fileA.Close();
                                fileB.Close();
                                return false;
                            }
                    }
                    fileA.Close();
                    fileB.Close();
                    return true;
                }
        }

        public static void ApplyDiff(string BaseVHD, string DiffVHD, string OutVHD = null, bool DifferencingOut = false, Tuple<int, int> Partition = null)
        {
            var DiffDisk = VirtualDisk.OpenDisk(DiffVHD, FileAccess.Read);
            VirtualDisk OutDisk;

            if (DifferencingOut)
            {
                using (var BaseDisk = VirtualDisk.OpenDisk(BaseVHD, FileAccess.Read))
                {
                    if (OutVHD == null || OutVHD.Equals(String.Empty))
                        throw new ArgumentNullException("OutVHD",
                                                        "OutVHD may not be null or empty when DifferencingOut is 'true'.");

                    OutDisk = BaseDisk.CreateDifferencingDisk(OutVHD);
                }
            }
            else
            {
                if (OutVHD != null)
                    File.Copy(BaseVHD, OutVHD);
                else
                    OutVHD = BaseVHD;

                OutDisk = VirtualDisk.OpenDisk(OutVHD, FileAccess.ReadWrite);
            }

            using (DiffDisk)
            using (OutDisk)
                if (Partition != null)
                {
                    var Base = OutDisk.Partitions[Partition.Item1];
                    var Diff = DiffDisk.Partitions[Partition.Item2];
                    ApplyPartDiff(Base, Diff);
                }
                else
                {
                    if (OutDisk.Partitions.Count != DiffDisk.Partitions.Count)
                        throw new ArgumentException(
                            "Input disks do not have the same number of partitions.  To apply the diff for specific partitions on mismatched disks, provide the 'Partition' parameter.");
                    for (int i = 0; i < OutDisk.Partitions.Count; i++)
                    {
                        if (OutDisk.Partitions[i].BiosType != DiffDisk.Partitions[i].BiosType)
                            throw new InvalidFileSystemException(
                                String.Format(
                                    "Filesystem of partition {0} in '{1}' does not match filesystem type of partition {0} in '{2}'.  Unable to apply diff.",
                                    i, DiffVHD, OutVHD));
                        ApplyPartDiff(OutDisk.Partitions[i], DiffDisk.Partitions[i]);
                    }
                }
        }

        private static void ApplyPartDiff(PartitionInfo Base, PartitionInfo Diff)
        {
            CopyQueue queue = new CopyQueue();

            var BFS = DetectFileSystem(Base);
            var DFS = DetectFileSystem(Diff);

            if (BFS is NtfsFileSystem)
            {
                ((NtfsFileSystem)BFS).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem)BFS).NtfsOptions.HideSystemFiles = false;
            }
            if (DFS is NtfsFileSystem)
            {
                ((NtfsFileSystem)DFS).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem)DFS).NtfsOptions.HideSystemFiles = false;
            }

            var DRoot = DFS.Root;

            var DFRoots = DRoot.GetDirectories(RootFiles);
            if (DFRoots.Any())
            {
                var DFileRoot = DFRoots.Single();

                foreach (var file in DFileRoot.GetFiles("*.*", SearchOption.AllDirectories))
                {
                    var BFile = BFS.GetFileInfo(file.FullName.Substring(RootFiles.Length + 1));
                    queue.Add(file, BFile); // TODO:  fix this for applying diffs!!!
                }
                queue.Go();
            }

            var DsysRegs = DRoot.GetDirectories(RootSystemRegistry);
            if (DsysRegs.Any())
            {
                var DsysReg = DsysRegs.Single();

                foreach (var file in DsysReg.GetFiles("*.*", SearchOption.AllDirectories))
                {
                    var BReg = BFS.GetFileInfo(file.FullName.Substring(RootSystemRegistry.Length + 1));
                    if (!BReg.Exists) 
                        queue.Add(file, BReg);
                    else
                    {
                        var BHive = new RegistryHive(BReg.Open(FileMode.Open, FileAccess.ReadWrite));
                        RegDiff.ReadFromStream(file.OpenRead()).ApplyTo(BHive.Root);
                    }
                }
                queue.Go();
            }

            var DuserRegs = DRoot.GetDirectories(RootUserRegistry);
            if (DuserRegs.Any())
            {
                var DuserReg = DuserRegs.Single();
                var Bfiles =
                    BFS.GetFiles(String.Empty, "*.*", SearchOption.AllDirectories)
                       .Where(str => UserRegisrtyFiles.IsMatch(str))
                       .ToArray();
                foreach (var file in DuserReg.GetFiles("*.*", SearchOption.AllDirectories))
                {
                    var username = DiffUserRegistry.Match(file.FullName).Groups["user"].Value;
                    var userFile = Bfiles.Where(str => GetUserRegex(username).IsMatch(str)).ToArray();
                    if (!userFile.Any()) continue;
                    var BReg = BFS.GetFileInfo(userFile.Single());
                    if (!BReg.Exists) continue;
                    var BHive = new RegistryHive(BReg.Open(FileMode.Open, FileAccess.ReadWrite));
                    RegDiff.ReadFromStream(file.OpenRead()).ApplyTo(BHive.Root);
                }
            }
        }

        // Extension method to handle partition formatting
        private static DiscFileSystem FormatPartition(this VirtualDisk Disk, int PartitionIndex)
        {
            var type = GetPartitionType(Disk.Partitions[PartitionIndex]);
            switch (type)
            {
                case WellKnownPartitionType.WindowsFat:
                    return FatFileSystem.FormatPartition(Disk, PartitionIndex, null);
                case WellKnownPartitionType.WindowsNtfs:
                    return NtfsFileSystem.Format(Disk.Partitions[PartitionIndex].Open(), null, Disk.Geometry,
                                                 Disk.Partitions[PartitionIndex].FirstSector,
                                                 Disk.Partitions[PartitionIndex].SectorCount);
                case WellKnownPartitionType.Linux:
                    // return ExtFileSystem.Format(...);
                default:
                    return null;
            }
        }

    }

    internal class CopyQueue
    {

        private class Lineage
        {
            public DiscFileInfo Base { get; private set; }
            public DiscFileInfo Source { get; private set; }
            public Lineage(DiscFileInfo source = null, DiscFileInfo @base = null)
            {
                Base = @base;
                Source = source;
            }
        }
        private XDictionary<DiscFileInfo, Lineage> _queue;
        public CopyQueue()
        {
            _queue = new XDictionary<DiscFileInfo, Lineage>();
        }
        public void Add(DiscFileInfo Source, DiscFileInfo Destination, DiscFileInfo Base = null)
        {
            _queue[Destination] = new Lineage(Source, Base);
        }
        public void Go()
        {
            while (_queue.Any())
                lock (this)
                {
                    if (!_queue.Any()) continue;
                    var current = _queue.First();
                    if (!current.Key.Directory.Exists)
                    {
                        CreateDirectoryTree(current.Value.Source.Directory, current.Key.Directory);
                    }
                    using (Stream src = current.Value.Source.OpenRead())
                        if (current.Key.Exists)  // trying to merge/apply data diff
                            if (Diff.IsBinary(src)) // binary blobs are copied whole
                                using (Stream dest = current.Key.Open(FileMode.Truncate, FileAccess.ReadWrite))
                                    src.CopyTo(dest);
                            else
                                using (Stream dest = current.Key.Open(FileMode.Open, FileAccess.ReadWrite))
                                {
                                    var srcBytes = src.toArray();
                                    Patch P = new Patch();

                                    P.ParseHunks(srcBytes);


                                    var newBytes = P.SimpleApply(dest.toArray());  // Replace this call with a more sophisticated (read "intellegent") diff application method.

                                    dest.Position = 0;
                                    dest.Write(newBytes, 0, newBytes.Length);
                                    dest.SetLength(newBytes.Length);
                                    
                                    /*
                                    MergeResult mr = current.Value.Base == null
                                                         ? MergeAlgorithm.merge(new RawText(dest.toArray()),
                                                                                new RawText(src.toArray()),
                                                                                new RawText(dest.toArray())) // use the base as "theirs"
                                                         : MergeAlgorithm.merge(new RawText(dest.toArray()),
                                                                                new RawText(src.toArray()),
                                                                                new RawText(
                                                                                    current.Value.Base.OpenRead()
                                                                                           .toArray()));
                                    bool conflicts = mr.containsConflicts();
                                    bool blurb = conflicts;

                                    */
                                }

                        else
                            using (var dest = current.Key.Create())
                                if (current.Value.Base == null || !current.Value.Base.Exists || Diff.IsBinary(src))
                                    src.CopyTo(dest);
                                else
                                {
                                    byte[] baseBytes = current.Value.Base.OpenRead().toArray();
                                    byte[] srcBytes = src.toArray();
                                    // string baseStr = baseBytes.Aggregate(String.Empty, (current1, b) => current1 + (char) b);
                                    // string srcStr = srcBytes.Aggregate(String.Empty, (current1, b) => current1 + (char)b);

                                    var diff = new Diff(baseBytes, srcBytes);

                                    if (diff.HasDifferences)
                                    {
                                        var df = new DiffFormatter();
                                        df.FormatEdits(dest, new RawText(baseBytes), new RawText(srcBytes), diff.GetEdits());
                                        /*
                                        Stream diffStream = new MemoryStream();
                                        df.FormatEdits(diffStream, new RawText(baseBytes), new RawText(srcBytes), diff.GetEdits());
                                        var fh = new CombinedFileHeader(Patch.ReadFully(diffStream), 0);
                                        var outStr = fh.getScriptText();
                                        byte[] bytes = outStr.Cast<byte>().ToArray();
                                        dest.Write(bytes, 0, bytes.Length);
                                        */
                                    }
                                    else // Not really different, just different metadata.  skip it.
                                    {
                                        dest.Close();
                                        current.Key.Delete();
                                        _queue.Remove(current.Key);
                                        continue;
                                    }
                                }


                    // set the attributes and file timestamps (and ACLs if it's ntfs...)
                    current.Key.Attributes = current.Value.Source.Attributes;
                    if (current.Key.FileSystem is NtfsFileSystem)
                    {
                        var D = (NtfsFileSystem) current.Key.FileSystem;
                        var S = (NtfsFileSystem) current.Value.Source.FileSystem;
                        D.SetSecurity(current.Key.FullName, S.GetSecurity(current.Value.Source.FullName));

                        D.SetFileStandardInformation(current.Key.FullName,
                                                     S.GetFileStandardInformation(current.Value.Source.FullName));
                    }
                    else
                    {
                        current.Key.CreationTimeUtc = current.Value.Source.CreationTimeUtc;
                        current.Key.LastWriteTimeUtc = current.Value.Source.LastWriteTimeUtc;
                        current.Key.LastAccessTimeUtc = current.Value.Source.LastAccessTimeUtc;
                    }


                    _queue.Remove(current.Key);
                }
        }

        public static void CreateDirectoryTree(DiscDirectoryInfo source, DiscDirectoryInfo destination)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (destination == null)
                throw new ArgumentNullException("destination");
            if (destination.Exists) return;

            if (!destination.Parent.Exists)
                CreateDirectoryTree(source.Parent, destination.Parent);

            destination.Create();
            destination.Attributes = source.Attributes;
            if (destination.FileSystem is NtfsFileSystem)
            {
                var D = (NtfsFileSystem) destination.FileSystem;
                var S = (NtfsFileSystem) source.FileSystem;
                D.SetSecurity(destination.FullName, S.GetSecurity(source.FullName));

                D.SetFileStandardInformation(destination.FullName, S.GetFileStandardInformation(source.FullName));
            }
            else
            {
                destination.CreationTimeUtc = source.CreationTimeUtc;
                destination.LastWriteTimeUtc = source.LastWriteTimeUtc;
                destination.LastAccessTimeUtc = source.LastAccessTimeUtc;
            }
        }

        public bool IsEmpty()
        {
            lock (this)
                return !_queue.Any();
        }

    }

}
