#pragma once
#include <Uefi.h>
#include <Protocol/DevicePath.h>
#include <Protocol/PartitionInfo.h>
#include <Guid/Gpt.h>
#include <string.h>
#include "palit_internal_config.h"
/* Exact GPT ESP identity on a non-USB, single-instance, bounded device path. */
static inline BOOLEAN PalitInternalVolume(EFI_SYSTEM_TABLE *table, EFI_HANDLE handle,
                                         const EFI_GUID *expected) {
  EFI_GUID pathGuid = EFI_DEVICE_PATH_PROTOCOL_GUID;
  EFI_GUID partitionGuid = EFI_PARTITION_INFO_PROTOCOL_GUID;
  EFI_GUID espGuid = EFI_PART_TYPE_EFI_SYSTEM_PART_GUID;
  EFI_DEVICE_PATH_PROTOCOL *path = NULL;
  EFI_PARTITION_INFO_PROTOCOL *partition = NULL;
  if (!table || !table->BootServices || !expected ||
      table->BootServices->HandleProtocol(handle, &pathGuid, (VOID **)&path) != EFI_SUCCESS || !path)
    return FALSE;
  UINTN bytes = 0;
  BOOLEAN ended = FALSE, hardDrive = FALSE;
  for (UINTN i = 0; i < 64 && bytes + 4 <= 4096; i++) {
    UINTN length = path->Length[0] + ((UINTN)path->Length[1] << 8);
    if (length < 4 || length > 4096 - bytes) return FALSE;
    if (path->Type == MESSAGING_DEVICE_PATH &&
        (path->SubType == MSG_USB_DP || path->SubType == MSG_USB_CLASS_DP || path->SubType == MSG_USB_WWID_DP))
      return FALSE;
    if (path->Type == MEDIA_DEVICE_PATH && path->SubType == MEDIA_HARDDRIVE_DP) {
      if (length != sizeof(HARDDRIVE_DEVICE_PATH)) return FALSE;
      hardDrive = TRUE;
    }
    if (path->Type == END_DEVICE_PATH_TYPE) {
      ended = length == 4 && path->SubType == END_ENTIRE_DEVICE_PATH_SUBTYPE;
      break;
    }
    bytes += length;
    path = (VOID *)((UINT8 *)path + length);
  }
  if (!ended || !hardDrive ||
      table->BootServices->HandleProtocol(handle, &partitionGuid, (VOID **)&partition) != EFI_SUCCESS || !partition)
    return FALSE;
  return partition->Revision == EFI_PARTITION_INFO_PROTOCOL_REVISION &&
         partition->Type == PARTITION_TYPE_GPT &&
         !memcmp(&partition->Info.Gpt.PartitionTypeGUID, &espGuid, sizeof(espGuid)) &&
         !memcmp(&partition->Info.Gpt.UniquePartitionGUID, expected, sizeof(*expected));
}
