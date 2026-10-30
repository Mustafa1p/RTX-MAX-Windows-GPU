# Build the Palit port from source

Requires Windows x64, **LLVM clang/lld-link** (reference: 20.1.8), Visual Studio C++ Build Tools and the Windows SDK. The EDK2 headers are included. Python 3.10+ is used by profile/result validation and staging. Bun is needed only for the preserved upstream tools/tests, not the Palit build wrapper.

No ready-to-boot binary/profile/assets are distributed. A successful build is not hardware qualification.

## Manual U + logging build

```powershell
.\tools\Build-Firmware.ps1 -Mode Manual -LlvmBin C:\Tools\LLVM\bin -OutputDirectory .\build\manual-local
```

The tool finds `vcvars64.bat` using Visual Studio's `vswhere` or the conventional VS 2022 BuildTools location. If necessary, supply it explicitly:

```powershell
.\tools\Build-Firmware.ps1 -Mode Manual `
    -LlvmBin C:\Tools\LLVM\bin `
    -VcVars 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat' `
    -OutputDirectory .\build\manual-local
```

## Internal automatic build

Identify the two ESPs locally. The variables below must contain the **actual distinct GUIDs** of a separately reviewed dedicated internal unlock ESP and the existing Windows ESP. This command does not discover/create them or verify the disk plan.

```powershell
.\tools\Build-Firmware.ps1 -Mode Internal `
    -InternalPartitionGuid $unlockEspGuid `
    -WindowsPartitionGuid $windowsEspGuid `
    -LlvmBin C:\Tools\LLVM\bin `
    -OutputDirectory .\build\internal-local
```

The public source's `palit_internal_config.h` is intentionally a refusing template. The wrapper copies source into its build directory and writes the local generated header there. Original source is not modified. The generated header and `build.json` contain local partition identities and must remain private.

Both modes emit `BOOTX64.EFI`, a link map and `build.json`. The wrapper checks AMD64 PE32+ EFI format, zero import/IAT/delay-import directories, required runtime paths and absence of automatic profile-mutation functions. **New builds are marked `hardware_tested: false`.** Different source/object paths or compiler/linker versions may produce a different image hash from the reference artifact. Do not reuse the reference hardware approval for a newly built image.

The manual mode requires U and logging. Internal mode adds the reviewed fixed automatic flow. Neither defines `GU_AUTO_PROFILE`; runtime profile creation stays disabled. Internal source and Windows handoff each pin their respective GPT partition, rejecting USB/malformed/ambiguous sources.

## Profile and firmware inputs

The preserved upstream utilities can build a read-only probe and generate a JSON profile from locally captured evidence. `runtime_profile.py` checks that JSON against its original local report/PROM paths before encoding. Inputs are private; do not put PROM.BIN, PROFILE.BIN or partition captures in a public repository. The root staging validator checks the binary's format/checksum/target but cannot reconstruct the original ownership graph.

The five firmware hashes/sizes are in [reference-artifacts.json](../results/reference-artifacts.json). Obtain them locally using [the upstream extraction tools](../firmware/tools/firmware/README.md), preserve the NVIDIA license and review its distribution terms. This public package contains no NVIDIA payloads.

## Host validation

```powershell
python -m unittest discover -s tests -v
powershell.exe -NoProfile -File tools/Test-BootParser.ps1
python tools/verify_results.py
python tools/check_package.py
```

For the preserved upstream 15-group suite, start a C++ developer shell with LLVM and Python available, then use Bun from `firmware/`:

```powershell
Set-Location firmware
bun install --frozen-lockfile
bun tools/test.ts
```

These host suites simulate hardware; they do not apply an unlock. The historical local port tests are recorded in [host-test-history.json](../results/host-test-history.json). Reference hardware tests are separately documented in [RESULTS.md](../RESULTS.md).
