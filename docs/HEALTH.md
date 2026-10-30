# Read-only health and installation checks

```powershell
.\Check-RTXMAX.ps1
.\Check-RTXMAX.ps1 -InstallRoot U:\
.\Check-RTXMAX.ps1 -InstallRoot U:\ -ManifestPath C:\LocalPackages\rtxmax\RTX-UNLOCK\installation.json
.\Check-RTXMAX.ps1 -NoReport
.\Check-RTXMAX.ps1 -NoReport -PassThru
```

The selected install root is read only. The default comparison manifest contains hashes of the tested **reference artifacts**. A locally rebuilt EFI can have a different hash; supply its staged `installation.json` when checking it. A manifest match proves file integrity against that manifest, not hardware approval or performance.

| Check | Meaning | Limit |
| --- | --- | --- |
| Windows device error | Device Manager error code, per adapter | Code 0 is not a compute benchmark |
| NVIDIA identity | Model, PCI/subsystem, driver and VBIOS | A matching reference signature does not prove board topology |
| Temperature | One driver reading | Below the project's 85°C advisory threshold does not certify cooling under load |
| Relevant events | Display/NVIDIA, WHEA and BugCheck providers since this Windows boot | No returned events is one signal, not a stability guarantee |
| Secure Boot/Fast Startup | Reads current/saved configuration | Does not change either setting |
| BitLocker | Mount point, encryption status, protection status | No recovery keys/protector objects collected |
| BootCurrent | Reads the actual UEFI boot option and device path | Requires elevation/UEFI; may be unavailable |
| Installation | EFI/profile/five asset hashes and internal partition association | Hidden/unmounted partitions can be missed |
| Unlock performance | Always unmeasured by this script | Requires a matched before/after compute test |

`PASS` is green, `WARN` yellow, `FAIL` red, `INFO` cyan and `UNKNOWN` gray. Missing telemetry, permissions and unsupported firmware APIs stay unknown. A nonmatching reference signature is a support warning, not a diagnosis of broken hardware.

The checker performs no GPU load, driver installation, cache clearing, overclock, boot edit, disk write outside its report, BitLocker change or reboot. Its C# helper declares a firmware **read** API only; a temporary process privilege is restored after reading.

Reports default to a newly named directory under `reports/`; existing report directories are not overwritten. They are gitignored. Local reports may include partition GUIDs, boot descriptions and software/hardware versions. Review/redact them before sharing. The published [health example](../results/health-example.json) is a sanitized excerpt, not an automatically uploaded live report.

Sources: [NVIDIA SMI monitoring/query reference](https://docs.nvidia.com/deploy/nvidia-smi/index.html), [Microsoft Secure Boot check](https://learn.microsoft.com/en-us/powershell/module/secureboot/confirm-securebootuefi).
