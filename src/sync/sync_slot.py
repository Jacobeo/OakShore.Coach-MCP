"""The home machine's SyncSlot: one sync at a time. Two runs share one Watermark and
post the same days, so the second is turned away rather than posting a day twice.

The slot is an operating-system lock rather than a flag written into a file, because
the system drops it when the process ends however it ends: a run killed part-way
cannot lock the machine out until a reboot."""

from __future__ import annotations

import os
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

if os.name == "nt":
    import msvcrt

    def _take_lock(handle: int) -> None:
        msvcrt.locking(handle, msvcrt.LK_NBLCK, 1)

    def _release_lock(handle: int) -> None:
        os.lseek(handle, 0, os.SEEK_SET)
        msvcrt.locking(handle, msvcrt.LK_UNLCK, 1)
else:
    import fcntl

    def _take_lock(handle: int) -> None:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)

    def _release_lock(handle: int) -> None:
        fcntl.flock(handle, fcntl.LOCK_UN)


class AlreadyRunning(RuntimeError):
    """Another sync holds the machine's one slot. Not a fault: the watermark means
    whatever this run would have done, the one already going will do."""


@contextmanager
def sync_slot(slot_file: Path) -> Iterator[None]:
    slot_file.parent.mkdir(parents=True, exist_ok=True)
    handle = os.open(slot_file, os.O_CREAT | os.O_RDWR)
    try:
        try:
            _take_lock(handle)
        except OSError:
            raise AlreadyRunning(
                f"Another sync is running on this machine ({slot_file})") from None
        try:
            yield
        finally:
            _release_lock(handle)
    finally:
        os.close(handle)
