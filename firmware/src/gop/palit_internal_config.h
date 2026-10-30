#pragma once
/* Public template: partition identities must come from the destination PC.
 * Build-Firmware.ps1 generates a private header in its build directory.
 * Never copy another computer's GUIDs into a production image.
 */
#error Internal automatic builds require a locally generated partition configuration
