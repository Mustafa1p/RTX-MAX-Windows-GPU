# Compatibility and qualification

| Configuration | Published evidence | Status |
| --- | --- | --- |
| Palit RTX 3060 Ti 8 GiB, PCI/subsystem 10de:2489, VBIOS 94.04.6b.00.f4, driver 610.88, ASRock B550M-HDV P3.40 / Ryzen 5 3600 | Three unlocked Windows sessions, matched compute comparison, 602-second mixed-load check | Verified on the reference PC |
| Same card name or PCI ID in another PC | No target-specific topology/profile/boot evidence | Unqualified |
| Different driver, VBIOS, motherboard or display route | No comparison in this distribution | Requalification required |
| Other RTX 30, RTX 40/50, laptop, AMD or Intel GPUs | No hardware tests supplied by this project | Unsupported by this tested port |

The preserved upstream code contains research paths for other configurations. Their existence is not a support promise from RTX-MAX. The Palit port opts into a specific shared physical GOP/TextOut layout, with exact runtime identity/profile and ownership checks. It does not suppress a generic refusal or guess a new profile.

A GPU signature match from `Check-RTXMAX.ps1` is only a starting observation. Qualification needs two matching read-only cold captures, review of the physical display/driver ownership graph, reviewed firmware/layout inputs, host rejection-path tests, a controlled manual UEFI transaction, display/Windows restoration, correctness-checked compute gains and a bounded load check. Internal automatic boot requires its own pinned partition identities and real boot-source verification.

Do not edit a JSON `hardwareTested`/`unlockApproved` flag to manufacture support. Build-time `hardware_tested: false` describes an untested artifact. Keep actual later hardware results in a separate record tied to its hash.
