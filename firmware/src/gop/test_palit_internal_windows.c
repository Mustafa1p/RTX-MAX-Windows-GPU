#define main InheritedWindowsTests
#include "test_windows.c"
#undef main
static EFI_PARTITION_INFO_PROTOCOL Partition;
static EFI_GUID PartitionGuid = EFI_PARTITION_INFO_PROTOCOL_GUID;
static BOOLEAN NoPartition, SecondDifferent;
static EFI_STATUS EFIAPI PinnedHandle(EFI_HANDLE h, EFI_GUID *g, VOID **out) {
  if (!memcmp(g, &PartitionGuid, sizeof(*g))) {
    *out = NoPartition ? NULL : &Partition;
    Partition.Info.Gpt.UniquePartitionGUID = (SecondDifferent && (UINTN)h == 2)
                                                ? PalitUnlockPartitionGuid : PalitWindowsPartitionGuid;
    return NoPartition ? EFI_UNSUPPORTED : EFI_SUCCESS;
  }
  return Handle(h, g, out);
}
static void PinnedSetup(void) {
  Setup();
  memset(&Partition, 0, sizeof(Partition));
  Partition.Revision = EFI_PARTITION_INFO_PROTOCOL_REVISION;
  Partition.Type = PARTITION_TYPE_GPT;
  EFI_GUID esp = EFI_PART_TYPE_EFI_SYSTEM_PART_GUID;
  Partition.Info.Gpt.PartitionTypeGUID = esp;
  Partition.Info.Gpt.UniquePartitionGUID = PalitWindowsPartitionGuid;
  NoPartition = SecondDifferent = FALSE;
  Bs.HandleProtocol = PinnedHandle;
}
int main(void) {
  PinnedSetup();
  assert(GwPrepare(&Context, &Plan) == EFI_SUCCESS && Loads == 1 && !Allocations);
  assert(GwCancel(&Context, &Plan) == EFI_SUCCESS);
  PinnedSetup(); NoPartition = TRUE;
  assert(GwPrepare(&Context, &Plan) == EFI_NOT_FOUND && !Loads && !Allocations);
  PinnedSetup(); Partition.Info.Gpt.PartitionTypeGUID.Data1 ^= 1;
  assert(GwPrepare(&Context, &Plan) == EFI_NOT_FOUND && !Loads && !Allocations);
  PinnedSetup(); Usb = TRUE;
  assert(GwPrepare(&Context, &Plan) == EFI_NOT_FOUND && !Loads && !Allocations);
  PinnedSetup(); Malformed = TRUE;
  assert(GwPrepare(&Context, &Plan) == EFI_NOT_FOUND && !Loads && !Allocations);
  PinnedSetup(); Count = Present = 2;
  assert(GwPrepare(&Context, &Plan) == EFI_UNSUPPORTED && !Loads && !Allocations);
  PinnedSetup(); Count = 2; Present = 1; SecondDifferent = TRUE;
  assert(GwPrepare(&Context, &Plan) == EFI_SUCCESS && Loads == 1 && !Allocations);
  assert(GwCancel(&Context, &Plan) == EFI_SUCCESS);
  PinnedSetup(); Present = 0;
  assert(GwPrepare(&Context, &Plan) == EFI_NOT_FOUND && !Loads && !Allocations);
  puts("PASS 8 internal Windows discovery cases: exact Windows ESP, foreign ESP ignored, ambiguity and malformed/USB/missing sources refused");
  return 0;
}
