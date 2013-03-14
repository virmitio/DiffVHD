using System;
using System.Management.Automation;
using ClrPlus.Powershell.Rest.Commands;

namespace DiffVHD
{

    /// <summary>
    /// Compares two virtual hard disks (VHD, VHDX, or VMDK) and produces a file-wise diff in the form of a VHD file.
    /// </summary>
    [Cmdlet(VerbsData.Compare, "VHD")]
    public class CompareVHD : RestableCmdlet<CompareVHD>
    {
        /// <summary>
        /// The 'Before' virtual hard disk for the comparison.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, HelpMessage = @"The 'Before' virtual hard disk for the comparison.")]
        [ValidateNotNullOrEmpty]
        public string Base;

        /// <summary>
        /// The 'After' virtual hard disk for the comparison.
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = @"The 'After' virtual hard disk for the comparison.")]
        [ValidateNotNullOrEmpty]
        public string Child;

        /// <summary>
        /// The filename on disk to write the output VHD to.
        /// </summary>
        [Parameter(Mandatory = true, Position = 2, HelpMessage = @"The filename on disk to write the output VHD to.")]
        [ValidateNotNullOrEmpty]
        public string Output;

        /// <summary>
        /// [Optional] If provided, only compares this (0-indexed) partition between the two virtual disks.  Default behaviour is to compare all partitions on the virtual disks.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] If provided, only compares this (0-indexed) partition between the two virtual disks.  Default behaviour is to compare all partitions on the virtual disks.")]
        [Alias("Partition1")]
        public int? Partition = null;

        /// <summary>
        /// [Optional] If provided along with <seealso cref="Partition"/>, will compare <seealso cref="Partition"/> from <seealso cref="Base"/> against this partition from <seealso cref="Child"/>.  Ignored if <seealso cref="Partition"/> is not present.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] If provided along with 'Partition', will compare 'Partition' from 'Base' against this partition from 'Child'.  Ignored if 'Partition' is not present.")]
        public int? Partition2 = null;

        /// <summary>
        /// [Optional] If set, will overwrite the output file if it exists.  If not set, will produce an error if the file already exists.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] If set, will overwrite the output file if it exists.  If not set, will produce an error if the file already exists.")]
        public SwitchParameter Overwrite = false;

        /// <summary>
        /// [Optional] The method of comparison to use.  Default is <seealso cref="DiffVHD.ComparisonStyle.DateTimeOnly"/>.  Valid comparison styles are:
        /// <list type="DiffVHD.ComparisonStyle">
        /// <item>NameOnly</item>
        /// <item>DateTimeOnly</item>
        /// <item>Journaled</item>
        /// <item>BinaryOnly</item>
        /// <item>Full</item>
        /// </list>
        /// </summary>
        [Parameter(
            HelpMessage=@"[Optional] The method of comparison to use.  Default is DateTimeOnly.  Valid comparison styles are:
        NameOnly
        DateTimeOnly
        Journaled
        BinaryOnly
        Full")]
        public DiffVHD.ComparisonStyle Comparison = DiffVHD.ComparisonStyle.DateTimeOnly;

        protected override void ProcessRecord()
        {
            // must use this to support processing record remotely.
            if (Remote)
            {
                ProcessRecordViaRest();
                return;
            }

            if (Partition.HasValue)
                if (Partition2.HasValue)
                    DiffVHD.CreateDiff(Base, Child, Output, Force: Overwrite,
                                       Partition: new Tuple<int, int>(Partition.Value, Partition2.Value), Style: Comparison);
                else DiffVHD.CreateDiff(Base, Child, Output, Partition, Force: Overwrite, Style: Comparison);
            else DiffVHD.CreateDiff(Base, Child, Output, Force: Overwrite, Style: Comparison);

        }
    }

    /// <summary>
    /// Attempts to apply the changes in a diff VHD generated from 'Compare-VHD' to a virtual hard disk.
    /// </summary>
    [Cmdlet("Apply", "VHDDiff")]
    public class ApplyVHDDiff : RestableCmdlet<ApplyVHDDiff>
    {
        /// <summary>
        /// The virtual disk to apply the differences to.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, HelpMessage = @"The virtual disk to apply the differences to.")]
        [ValidateNotNullOrEmpty]
        public string Base;

        /// <summary>
        /// The diff VHD to apply differences from.
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = @"The diff VHD to apply differences from.")]
        [ValidateNotNullOrEmpty]
        public string Diff;
        
        /// <summary>
        /// The new output file (if any) to generate.
        /// </summary>
        [Parameter(Mandatory = true, Position = 2, HelpMessage = @"The new output file (if any) to generate.")]
        public string Output = null;

        /// <summary>
        /// If set, the <seealso cref="Output"/> file generated will be a differencing disk with <seealso cref="Base"/> set as the parent.  Otherwise the differences will be applied directly to <seealso cref="Base"/> (if no <seealso cref="Output"/> is set) or to a copy of <seealso cref="Base"/>.
        /// </summary>
        [Parameter(HelpMessage = @"If set, the <Output> file generated will be a differencing disk with <Base> set as the parent.  Otherwise the differences will be applied directly to <Base> (if no <Output> is set) or to a copy of <Base>.")]
        public SwitchParameter MakeDifferencingDisk = false;

        /// <summary>
        /// [Optional] Only applies differences to this partition of the base disk from this partition of the diff VHD.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] Only applies differences to this partition of the base disk from this partition of the diff VHD.")]
        [Alias("Partition1")]
        public int? Partition = null;

        /// <summary>
        /// [Optional] Only applies differences from this partition of the diff VHD to <seealso cref="Partition"/> partition of the base disk.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] Only applies differences from this partition of the diff VHD to <Partition> partition of the base disk.")]
        public int? Partition2 = null;

        /// <summary>
        /// [Optional] If set, will overwrite the output file if it exists.  If not set, will produce an error if the file already exists.
        /// </summary>
        [Parameter(HelpMessage = @"[Optional] If set, will overwrite the output file if it exists.  If not set, will produce an error if the file already exists.")]
        public SwitchParameter Overwrite = false;

        protected override void ProcessRecord()
        {
            // must use this to support processing record remotely.
            if (Remote)
            {
                ProcessRecordViaRest();
                return;
            }

            if (!Overwrite && Output == null)
            {
                WriteWarning(
                    "Unable to continue.  Must provide an output location or specify '-Overwrite' to replace base image.");
                return;
            }

            if (Partition.HasValue)
                if (Partition2.HasValue) DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk, new Tuple<int, int>(Partition.Value, Partition2.Value));
                else DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk, new Tuple<int,int>(Partition.Value, Partition.Value));
            else DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk);

        }
    }

}
