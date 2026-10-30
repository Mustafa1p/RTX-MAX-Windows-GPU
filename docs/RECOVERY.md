# Return to normal Windows startup

Keep your BitLocker recovery key private and accessible without this PC before changing the boot path. Switching from the measured unlock chain to a different Windows boot route can require that key. Do not clear the TPM, delete protectors or publish a key in a bug report.

For a failed experimental boot, fully power off, then use the motherboard's boot menu to choose **Windows Boot Manager**. The reference ASRock board uses F11; other boards differ. Have the recovery key available if Windows requests it.

For a controlled permanent change, first review bounded BitLocker protection suspension, set Windows Boot Manager first, retain the unlock entry/files while verifying normal Windows startup, then restore protection and confirm its status. There is no need to erase a partition or overwrite the Windows loader to change the default entry. Do not delete files from a mounted drive solely because its letter resembles the reference setup.

On the original reference PC, the private recovery helper and backed-up boot order remain in the original workspace. They are deliberately not distributed as a generic installer/recovery script because their identities belong to that machine. Root health/staging tools in this public package never alter boot entries or encryption.

Sources: [Microsoft Suspend-BitLocker](https://learn.microsoft.com/en-us/powershell/module/bitlocker/suspend-bitlocker), [Microsoft Resume-BitLocker](https://learn.microsoft.com/en-us/powershell/module/bitlocker/resume-bitlocker).
