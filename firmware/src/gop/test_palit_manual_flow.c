/* Run the actual candidate's app/profile/confirmation path with inert GPU stubs. */
#define EXTERNAL_FLOW_MAIN InheritedExternalFlowChecks
#include "test_external_flow.c"

static void ManualSetup(void) {
  ExternalSetup();
  DeviceIdentity = 0x248910deU;
  DeviceSubsystem = 0x248910deU;
}
int main(int argc, char **argv) {
  assert(argc == 2);
  FILE *file = fopen(argv[1], "rb");
  assert(file && fread(ProfileBytes, 1, sizeof(ProfileBytes), file) == sizeof(ProfileBytes));
  fclose(file);
  ManualSetup();
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(Starts == 1 && Runs == 1 && Plans == 1 && Polls == 1 && Writes > 0 &&
         GuProfile.identity == 0x248910deU &&
         strstr(SavedLog, "MANUAL_U") && !strstr(SavedLog, "AUTO_START"));
  ManualSetup(); DebugMarker = TRUE;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(Starts == 1 && Runs == 1 && Polls == 1);
  ManualSetup(); Key = 0x1b;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Runs && Polls == 1);
  ManualSetup(); InputStatus = EFI_NOT_READY;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Runs && Polls == 300);
  ManualSetup(); MissingProfile = TRUE;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Plans && !Runs && !Polls && !GuProfile.identity);
  ManualSetup(); CorruptProfile = TRUE;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Plans && !Runs && !Polls && !GuProfile.identity);
  ManualSetup(); Skip = TRUE; Key = 0x1b;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Plans && !Runs && Polls == 1);
  ManualSetup(); Skip = TRUE;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(Starts == 1 && !Plans && !Runs && Polls == 1);
  ManualSetup(); PreflightStatus = EFI_NOT_READY;
  if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System);
  assert(!Starts && !Plans && !Runs && !Polls);
  puts("PASS 9 actual Palit manual app flows: U mandatory without DEBUG, Esc/timeout, profile corruption and preflight failure; GPU/Windows mocked");
  return 0;
}
