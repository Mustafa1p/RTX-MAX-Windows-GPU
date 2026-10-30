# Attribution and distribution scope

RTX-MAX Windows GPU is a downstream documentation/tooling package and local Palit port. It is not an official NVIDIA, Palit, Microsoft or upstream release.

| Component | Source | Terms / retained notice |
| --- | --- | --- |
| UEFI implementation and tooling base | [craftingmod/rtx30-unlock](https://github.com/craftingmod/rtx30-unlock/tree/5c076dc4ee7fa369639669fa591ac74d8f6dfcd8) | GPL-2.0-only; source headers and [original notices](UPSTREAM-NOTICES.md) retained |
| Original requested project / research reference | [impacifist/Windows-GPU-UNLOCK](https://github.com/impacifist/Windows-GPU-UNLOCK/tree/75071aa134616c3d65ad315e0cf2810bf34b191b) | Referenced, not bundled as the implemented Palit runtime |
| Core and port lineage | [Zagitta/gpu-unlock](https://github.com/Zagitta/gpu-unlock), [rough2515-hub/rtx3080-unlock-public](https://github.com/rough2515-hub/rtx3080-unlock-public) | See upstream notices and retained copyright headers |
| EDK2 headers | TianoCore EDK2 pinned by upstream | BSD-2-Clause-Patent; license and headers retained under firmware/ |
| NVIDIA/CMP reference code | Sources credited by upstream | Their original notices retained under firmware/LICENSES/ |
| Local Palit port, new diagnostics, documentation and artwork | Local changes dated 2026-10-30 | GPL-2.0-only, subject to retained component notices |

Local modifications implement the exact shared physical GOP/TextOut layout and fixed internal partition-aware automatic startup tested on the reference PC. The public internal GUID header is a refusing template. New scripts provide read-only diagnostics, file staging, result verification and build/package validation. See [source provenance](results/source-provenance.json).

NVIDIA firmware payloads, runtime profiles, GPU ROM dumps and compiled EFI images are absent. NVIDIA firmware is not licensed by the GPL source notice. Its separate agreement and the upstream [distribution-scope note](firmware/tools/firmware/NOTICE.md) remain relevant to any local extraction or later redistribution.
