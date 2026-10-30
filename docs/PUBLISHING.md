# Publish this folder on GitHub

This folder has been prepared locally; no GitHub repository or release is created automatically.

1. Read README.md, README.ar.md and the support scope. Suggested repository slug: `rtx-max-windows-gpu`.
2. Run `python tools/check_package.py`, `python tools/verify_results.py`, the Python tests and `tools/Test-BootParser.ps1`.
3. Keep `reports/`, `build/`, `node_modules/`, local captures and all private recovery material out of Git. `.gitignore` is provided; inspect the staged file list as well.
4. Create an empty GitHub repository. Use Git, or extract the prepared clean source ZIP for a browser upload. Browser uploads do not automatically enforce `.gitignore`; leave out cache/build/report folders yourself. Do not upload a complete drive/ESP copy or firmware payloads.

```powershell
git init
git add .
git status --short
git diff --cached --stat
```

After reviewing that list, commit, add your actual repository URL and push using your usual GitHub workflow. The project does not assume your username or create a repository on your behalf.

Recommended description: **Documented Palit RTX 3060 Ti UEFI experiment, measured Tensor compute gains, source and read-only Windows diagnostics.**

Use [assets/social-preview.png](../assets/social-preview.png) as the repository social preview. All README artwork is local PNG, so no external image service or tracking URL is required. Charts are generated from published measurements. The boot diagram is a schematic, and the health image is a rendered excerpt of real diagnostic findings, not a firmware screenshot.

The supplied CI workflow checks archived results, rejection tests, PowerShell parsing and the pure boot-option parser. It does not run an actual UEFI unlock, prove GPU support or claim hardware test coverage for a new release.

Preserve upstream attribution, copyright headers, GPL license and component notices. Review NVIDIA's separate terms before redistributing any firmware; no such redistribution approval is claimed here. [GitHub's image-format documentation](https://docs.github.com/en/repositories/working-with-files/using-files/working-with-non-code-files) describes supported repository images.

The attributes file deliberately preserves exact bytes under `firmware/`, `results/raw/` and `results/measurement-source/`. Changing their line endings would invalidate the recorded source/evidence hashes.
