/* Real entry/profile/source/approval code; GPU transaction and OS remain inert. */
#define EXTERNAL_FLOW_MAIN InheritedExternalFlowChecks
#include "test_external_flow.c"
static EFI_PARTITION_INFO_PROTOCOL InternalPartition;
static struct { HARDDRIVE_DEVICE_PATH hd; EFI_DEVICE_PATH_PROTOCOL end; } InternalPath;
static BOOLEAN AutoLogMarker, AutoLogError, NoPartition, BadAutoLogClose;
static EFI_GUID PartitionGuid = EFI_PARTITION_INFO_PROTOCOL_GUID;
static EFI_FILE_PROTOCOL AutoLogFile;
static EFI_STATUS EFIAPI InternalHandle(EFI_HANDLE h, EFI_GUID *g, VOID **out) {
  if (!memcmp(g, &PathGuid, sizeof(*g))) { *out = &InternalPath; return EFI_SUCCESS; }
  if (!memcmp(g, &PartitionGuid, sizeof(*g))) {
    *out = NoPartition ? NULL : &InternalPartition;
    return NoPartition ? EFI_UNSUPPORTED : EFI_SUCCESS;
  }
  return StartupHandle(h, g, out);
}
static EFI_STATUS EFIAPI InternalOpen(EFI_FILE_PROTOCOL *d, EFI_FILE_PROTOCOL **out,
                                      CHAR16 *name, UINT64 mode, UINT64 attributes) {
  if (name[0] == 'A') {
    static const CHAR16 expected[] = L"AUTOLOG.ON";
    assert(!memcmp(name, expected, sizeof(expected)) && mode == EFI_FILE_MODE_READ && !attributes);
    *out = AutoLogMarker ? &AutoLogFile : NULL;
    if (AutoLogError) { *out = NULL; return EFI_DEVICE_ERROR; }
    return AutoLogMarker ? EFI_SUCCESS : EFI_NOT_FOUND;
  }
  return ProfileOpen(d, out, name, mode, attributes);
}
static EFI_STATUS EFIAPI InternalClose(EFI_FILE_PROTOCOL *file) {
  if (file == &AutoLogFile && BadAutoLogClose) return EFI_DEVICE_ERROR;
  return FileClose(file);
}
static void InternalSetup(void) {
  ExternalSetup();
  DeviceIdentity = DeviceSubsystem = 0x248910deU;
  AutoLogMarker = AutoLogError = NoPartition = BadAutoLogClose = FALSE;
  AutoLogFile.Close = InternalClose;
  ParentFile.Open = InternalOpen;
  memset(&InternalPartition, 0, sizeof(InternalPartition));
  InternalPartition.Revision = EFI_PARTITION_INFO_PROTOCOL_REVISION;
  InternalPartition.Type = PARTITION_TYPE_GPT;
  EFI_GUID esp = EFI_PART_TYPE_EFI_SYSTEM_PART_GUID;
  InternalPartition.Info.Gpt.PartitionTypeGUID = esp;
  InternalPartition.Info.Gpt.UniquePartitionGUID = PalitUnlockPartitionGuid;
  memset(&InternalPath, 0, sizeof(InternalPath));
  InternalPath.hd.Header.Type = MEDIA_DEVICE_PATH;
  InternalPath.hd.Header.SubType = MEDIA_HARDDRIVE_DP;
  InternalPath.hd.Header.Length[0] = sizeof(InternalPath.hd);
  InternalPath.end.Type = END_DEVICE_PATH_TYPE;
  InternalPath.end.SubType = END_ENTIRE_DEVICE_PATH_SUBTYPE;
  InternalPath.end.Length[0] = 4;
  Services.HandleProtocol = InternalHandle;
}
static void RunEntry(void) { if (!setjmp(ExitFlow)) efi_main((EFI_HANDLE)(UINTN)77, &System); }
int main(int argc, char **argv) {
  assert(argc == 2);
  FILE *file = fopen(argv[1], "rb");
  assert(file && fread(ProfileBytes, 1, sizeof(ProfileBytes), file) == sizeof(ProfileBytes));
  fclose(file);
  InternalSetup(); RunEntry();
  assert(Starts == 1 && Runs == 1 && Plans == 1 && !Polls && !Writes && AutomaticStart);
  InternalSetup(); AutoLogMarker = TRUE; RunEntry();
  assert(Starts == 1 && Runs == 1 && !Polls && Writes && strstr(SavedLog, "AUTO_START") && !strstr(SavedLog, "MANUAL_U"));
  InternalSetup(); DebugMarker = AutoLogMarker = TRUE; RunEntry();
  assert(Starts == 1 && Runs == 1 && Polls == 1 && !AutomaticStart && strstr(SavedLog, "MANUAL_U"));
  InternalSetup(); DebugMarker = TRUE; Key = 0x1b; RunEntry();
  assert(!Starts && !Runs && Polls == 1);
  InternalSetup(); DebugMarker = TRUE; InputStatus = EFI_NOT_READY; RunEntry();
  assert(!Starts && !Runs && Polls == 300);
  InternalSetup(); MissingProfile = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs && !Polls);
  InternalSetup(); CorruptProfile = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs && !Polls);
  InternalSetup(); InternalPartition.Info.Gpt.UniquePartitionGUID.Data1 ^= 1; RunEntry();
  assert(!Starts && !Plans && !Runs && !Polls);
  InternalSetup(); InternalPartition.Info.Gpt.UniquePartitionGUID = PalitWindowsPartitionGuid; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); InternalPartition.Info.Gpt.PartitionTypeGUID.Data1 ^= 1; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); NoPartition = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); InternalPath.hd.Header.Type = MESSAGING_DEVICE_PATH; InternalPath.hd.Header.SubType = MSG_USB_DP; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); InternalPath.end.SubType = END_INSTANCE_DEVICE_PATH_SUBTYPE; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); AutoLogError = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); AutoLogMarker = BadAutoLogClose = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); AutoLogMarker = FailWrite = TRUE; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); SecureEnabled = 1; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); PreflightStatus = EFI_NOT_READY; RunEntry();
  assert(!Starts && !Plans && !Runs);
  InternalSetup(); RunStatus = EFI_DEVICE_ERROR; RunEntry();
  assert(!Starts && Plans == 1 && Runs == 1);
  InternalSetup(); Skip = TRUE; AutoLogMarker = TRUE; RunEntry();
  assert(Starts == 1 && !Plans && !Runs && !Polls && strstr(SavedLog, "PATCH_SKIP"));
  puts("PASS 20 internal automatic entry cases: exact ESP, auto with/without logs, debug U, corrupt profile, source/secure-boot/log/transaction refusals; GPU/OS mocked");
  return 0;
}
