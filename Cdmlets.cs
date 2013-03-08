using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClrPlus.Core.Collections;
using ClrPlus.Platform;
using ClrPlus.Powershell.Rest.Commands;

namespace DiffVHD
{

    [Cmdlet(VerbsData.Compare, "VHD")]
    public class CompareVHD : RestableCmdlet<CompareVHD>
    {
        [Parameter(Mandatory = true, Position = 0)] [ValidateNotNullOrEmpty] public string VHD1;

        [Parameter(Mandatory = true, Position = 1)] [ValidateNotNullOrEmpty] public string VHD2;

        [Parameter(Mandatory = true, Position = 2)] [ValidateNotNullOrEmpty] public string Output;

        [Parameter] [Alias("Partition1")] public int? Partition = null;

        [Parameter] public int? Partition2 = null;

        [Parameter] public SwitchParameter Overwrite = false;

        [Parameter] public DiffVHD.ComparisonStyle Comparison = DiffVHD.ComparisonStyle.DateTimeOnly;

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
                    DiffVHD.CreateDiff(VHD1, VHD2, Output, Force: Overwrite,
                                       Partition: new Tuple<int, int>(Partition.Value, Partition2.Value), Style: Comparison);
                else DiffVHD.CreateDiff(VHD1, VHD2, Output, Partition, Force: Overwrite, Style: Comparison);
            else DiffVHD.CreateDiff(VHD1, VHD2, Output, Force: Overwrite, Style: Comparison);

        }
    }

    [Cmdlet("Apply", "VHDDiff")]
    public class ApplyVHDDiff : RestableCmdlet<ApplyVHDDiff>
    {
        [Parameter(Mandatory = true, Position = 0)] [ValidateNotNullOrEmpty] public string Base;

        [Parameter(Mandatory = true, Position = 1)] [ValidateNotNullOrEmpty] public string Diff;

        [Parameter(Mandatory = true, Position = 2)] public string Output = null;

        [Parameter] public SwitchParameter MakeDifferencingDisk = false;

        [Parameter] [Alias("Partition1")] public int? Partition = null;

        [Parameter] public int? Partition2 = null;

        [Parameter] public SwitchParameter Overwrite = false;

        protected override void ProcessRecord()
        {
            // must use this to support processing record remotely.
            if (Remote)
            {
                ProcessRecordViaRest();
                return;
            }

            if (Partition.HasValue)
                if (Partition2.HasValue) DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk, new Tuple<int, int>(Partition.Value, Partition2.Value));
                else DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk, new Tuple<int,int>(Partition.Value, Partition.Value));
            else DiffVHD.ApplyDiff(Base, Diff, Output, MakeDifferencingDisk);

        }
    }

}
