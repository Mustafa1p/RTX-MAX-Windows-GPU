/* Direct GOP/TextOut ownership model derived from the captured four-node tree.
 * All protocols, PCI access, driver callbacks and reset effects are RAM mocks.
 */
#define GC_PATCH_FIXTURE
#include "test_full.c"
static BOOLEAN ExtraGopChild, DuplicateGopOwner, MissingText, KeepTextOwner, ChangeOnRestore;
#ifdef GC_FULL_UNLOCK
#include "patch.h"
#include "visual.h"
static GF_PLAN *DirectPlan;
static unsigned DirectPatchCalls;
static BOOLEAN DirectPatchSafe, DirectPatchError;
EFI_STATUS GxPatch(BOOLEAN (*check)(VOID)) {
  assert(++DirectPatchCalls == 1 && check && check());
  BOOLEAN attempted = FALSE;
  EFI_STATUS status = GfResetPci(&Context, &DirectPlan->pci, &attempted);
  DirectPatchSafe = !EFI_ERROR(status) && attempted && !DirectPatchError;
  return DirectPatchError ? EFI_DEVICE_ERROR : status;
}
BOOLEAN GxSafe(VOID) { return DirectPatchSafe; }
VOID GxReleased(VOID) { assert(!Exclusive && DirectPatchSafe); }
EFI_STATUS GxObserve(GC_CONTEXT *c, const char *phase) {
  assert(c == &Context && phase && DirectPatchSafe);
  return EFI_SUCCESS;
}
BOOLEAN GvMark(GC_CONTEXT *c, const char *phase) {
  assert(c == &Context && phase);
  return TRUE;
}
EFI_STATUS GvDisplay(GC_CONTEXT *c, const GL_PLAN *old, const GL_PLAN *fresh) {
  assert(c == &Context && old->physical_text && fresh->physical_text && Bits == 3);
  return EFI_SUCCESS;
}
#endif
static EFI_STATUS EFIAPI DHandle(EFI_HANDLE h, EFI_GUID *g, VOID **out) {
  if (Original(h) == PHYS && Eq(g, &TextGuid)) {
    *out = ChildrenOn && !MissingText ? &LocalText : NULL;
    return *out ? EFI_SUCCESS : EFI_UNSUPPORTED;
  }
  if (Original(h) == TEXT) {
    *out = NULL;
    return EFI_UNSUPPORTED;
  }
  return FHandle(h, g, out);
}
static EFI_STATUS EFIAPI DProtocols(EFI_HANDLE h, EFI_GUID ***out, UINTN *n) {
  if (Original(h) == PHYS && ChildrenOn) {
    *n = 4;
    *out = Alloc(*n * sizeof(**out));
    (*out)[0] = &GopGuid;
    (*out)[1] = &TextGuid;
    (*out)[2] = &EdidGuid;
    (*out)[3] = &PathGuid;
    return EFI_SUCCESS;
  }
  if (Original(h) == TEXT) {
    *out = NULL;
    *n = 0;
    return EFI_NOT_FOUND;
  }
  return FProtocols(h, out, n);
}
static EFI_STATUS EFIAPI DOpens(EFI_HANDLE h, EFI_GUID *g,
                                EFI_OPEN_PROTOCOL_INFORMATION_ENTRY **out, UINTN *n) {
  if (Original(h) != PHYS) return FOpens(h, g, out, n);
  EFI_OPEN_PROTOCOL_INFORMATION_ENTRY entries[4] = {0};
  *n = 0;
  *out = NULL;
  if (Eq(g, &GopGuid)) {
    if (BridgeOn) Add(entries, n, BRIDGE, h, EFI_OPEN_PROTOCOL_BY_DRIVER);
    if (BridgeOn && DuplicateGopOwner)
      Add(entries, n, H(98), h, EFI_OPEN_PROTOCOL_BY_DRIVER);
    if (BridgeOn && ExtraGopChild)
      Add(entries, n, BRIDGE, Current(TEXT), EFI_OPEN_PROTOCOL_BY_CHILD_CONTROLLER);
  } else if (Eq(g, &TextGuid)) {
    if (Bits & 1) Add(entries, n, MEMBER, h, EFI_OPEN_PROTOCOL_BY_DRIVER);
    if (Bits & 2)
      Add(entries, n, WrongOwner ? VENDOR : MEMBER, WrongChild ? EDID : SHARED,
          EFI_OPEN_PROTOCOL_BY_CHILD_CONTROLLER);
  } else if (Eq(g, &EdidGuid) && EdidP) {
    Add(entries, n, RUNTIME, h, EFI_OPEN_PROTOCOL_BY_DRIVER);
  }
  if (*n) {
    *out = Alloc(*n * sizeof(**out));
    memcpy(*out, entries, *n * sizeof(**out));
  }
  return EFI_SUCCESS;
}
static EFI_STATUS EFIAPI DStop(EFI_DRIVER_BINDING_PROTOCOL *binding, EFI_HANDLE parent,
                               UINTN n, EFI_HANDLE *children) {
  unsigned role = Actor(binding->DriverBindingHandle);
  if (role != 1 && role != 3) return FStop(binding, parent, n, children);
  FullStops++;
  assert(!Exclusive && !Flrs && Original(parent) == PHYS);
  if (FailStopRole == role + 1) return EFI_DEVICE_ERROR;
  if (role == 3) {
    if (n) {
      assert(n == 1 && children[0] == SHARED && Bits == 3);
      Bits = KeepTextOwner ? 1 : 0;
    } else {
      assert(Bits == 1);
      Bits = 0;
    }
  } else {
    assert(!n && !Bits && !AggOn && !BridgeChild && BridgeOn);
    BridgeOn = FALSE;
  }
  return EFI_SUCCESS;
}
static EFI_STATUS EFIAPI DStart(EFI_DRIVER_BINDING_PROTOCOL *binding, EFI_HANDLE parent,
                                EFI_DEVICE_PATH_PROTOCOL *path) {
  unsigned role = Actor(binding->DriverBindingHandle);
  if (role != 1 && role != 3) return FStart(binding, parent, path);
  FullStarts++;
  assert(!path && !Exclusive && Flrs == 1 && Original(parent) == PHYS);
  if (role == 1) {
    assert(!BridgeOn);
    BridgeOn = TRUE;
    if (ChangeOnRestore) ExtraGopChild = TRUE;
  } else {
    assert(BridgeOn && !AggOn);
    Bits = 3;
  }
  return EFI_SUCCESS;
}
static VOID DReset(VOID) {
  FReset();
  BridgeChild = AggOn = FALSE;
  ExtraGopChild = DuplicateGopOwner = MissingText = KeepTextOwner = ChangeOnRestore = FALSE;
#ifdef GC_FULL_UNLOCK
  DirectPlan = NULL;
  DirectPatchCalls = 0;
  DirectPatchSafe = DirectPatchError = FALSE;
#endif
  Context.identity = Config[0] = 0x248910deU;
  Context.subsystem = Config[11] = 0x248910deU;
  Bs.HandleProtocol = DHandle;
  Bs.ProtocolsPerHandle = DProtocols;
  Bs.OpenProtocolInformation = DOpens;
  for (unsigned i = 0; i < 5; i++) {
    Fb[i].Stop = DStop;
    Fb[i].Start = DStart;
  }
}
static VOID DPrepare(GF_PLAN *p) {
  assert(GfPrepare(&Context, p) == EFI_SUCCESS);
  assert(p->leaf.physical_text && p->leaf.physical == p->leaf.text);
  assert(!p->drivers[2].driver.handle && !FullStops && !Flrs && !Allocations);
#ifdef GC_FULL_UNLOCK
  DirectPlan = p;
#endif
}
int main(void) {
  (void)FPrepare; /* Inherited aggregate fixture helper is intentionally not called. */
  GF_PLAN *p = malloc(sizeof(*p));
  assert(p);
  GC_RESULT r;
#ifndef GC_SHARED_TEXT_GOP
  (void)DPrepare;
  DReset();
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs && !Allocations);
  free(p);
  puts("PASS direct GOP/TextOut is refused without the explicit build option");
  return 0;
#endif
  unsigned checks = 0;
  for (unsigned mode = 0; mode < 3; mode++) {
    DReset();
    Recreate = mode == 1;
    KeepTextOwner = mode == 2;
    DPrepare(p);
    assert(GfRun(&Context, p, &r) == EFI_SUCCESS && r.restored && Flrs == 1 &&
           QuietWaits == 1 && !Exclusive && !Allocations && BridgeOn && Bits == 3);
    checks++;
  }
  DReset(); ExtraGopChild = TRUE;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); DuplicateGopOwner = TRUE;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); MissingText = TRUE;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); WrongOwner = TRUE;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); WrongChild = TRUE;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); Config[1] |= 4;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); Context.identity = Config[0] = 0x248410deU;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); Context.subsystem = Config[11] = 0x123410deU;
  assert(EFI_ERROR(GfPrepare(&Context, p)) && !FullStops && !Flrs); checks++;
  DReset(); DPrepare(p); Config[1] |= 4;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && !FullStops && !r.touched && !Flrs); checks++;
  unsigned stop_roles[] = {1, 2, 4, 5};
  for (unsigned i = 0; i < 4; i++) {
    DReset(); DPrepare(p); FailStopRole = stop_roles[i];
    assert(EFI_ERROR(GfRun(&Context, p, &r)) && !Flrs && !r.restored && !Allocations);
    checks++;
  }
  unsigned start_roles[] = {1, 2, 4, 5};
  for (unsigned i = 0; i < 4; i++) {
    DReset(); DPrepare(p); FailStartRole = start_roles[i];
    assert(EFI_ERROR(GfRun(&Context, p, &r)) && Flrs == 1 && !r.restored && !Allocations);
    checks++;
  }
  DReset(); DPrepare(p); ChangeRuntimeInput = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && !Flrs && VendorOn); checks++;
  DReset(); DPrepare(p); ExposeGop = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && !Flrs); checks++;
  DReset(); DPrepare(p); ResidualOwner = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && !Flrs && r.claim == EFI_NOT_STARTED); checks++;
  DReset(); DPrepare(p); ChangeOnRestore = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && Flrs == 1 && !r.restored); checks++;
  DReset(); DPrepare(p); FailLogRun = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && !Flrs); checks++;
  DReset(); DPrepare(p); FailClose = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && Flrs == 1 && Exclusive && !FullStarts); checks++;
#ifdef GC_FULL_UNLOCK
  DReset(); DPrepare(p); DirectPatchError = TRUE;
  assert(EFI_ERROR(GfRun(&Context, p, &r)) && DirectPatchCalls == 1 && !DirectPatchSafe &&
         !FullStarts && !r.restored && Exclusive); checks++;
#endif
  assert(!Allocations);
  free(p);
  printf("PASS %u direct GOP/TextOut prepare/release/reset/restore/fault cases; RAM-only EFI mocks\n", checks);
  return 0;
}
