"""Dependency-free simulated VapourSynth tests for the bundled motion scripts.

Validates crop/padding and FPS for all supported scripts without GPU/OpenCL.
This test does NOT prove that real SVPflow plugins succeed on a video stream.
"""
from fractions import Fraction
from pathlib import Path
import runpy
import sys
import types
import unittest

ROOT = Path(__file__).resolve().parents[1]
MOTION = ROOT / "src" / "HDREZKA.App" / "motion"


class FakeFormat:
    color_family = "yuv"
    subsampling_w = 1
    subsampling_h = 1


class FakeClip:
    def __init__(self, core, width, height, fps=Fraction(24000, 1001)):
        self.core = core
        self.width = width
        self.height = height
        self.fps = fps
        self.format = FakeFormat()

    def copy(self, width=None, height=None, fps=None):
        return FakeClip(
            self.core,
            self.width if width is None else width,
            self.height if height is None else height,
            self.fps if fps is None else fps,
        )

    def set_output(self):
        self.core.output = self


class FakeStd:
    def __init__(self, core):
        self.core = core

    def LoadPlugin(self, path):
        pass

    def AssumeFPS(self, clip, fpsnum, fpsden):
        return clip.copy(fps=Fraction(fpsnum, fpsden))

    def AddBorders(self, clip, right, bottom):
        assert right >= 0 and bottom >= 0
        assert right % 2 == 0 and bottom % 2 == 0
        self.core.borders.append((right, bottom))
        return clip.copy(width=clip.width + right, height=clip.height + bottom)

    def CropRel(self, clip, right, bottom):
        assert right >= 0 and bottom >= 0
        self.core.crops.append((right, bottom))
        return clip.copy(width=clip.width - right, height=clip.height - bottom)


class FakeSvp1:
    def __init__(self, core):
        self.core = core

    def Halve(self, clip):
        # Treat odd chroma plane sizes as failures, reproducing the edge case.
        assert clip.width % 4 == 0 and clip.height % 4 == 0, (
            "Halved YUV420 must have even dimensions")
        self.core.analysis.append((clip.width // 2, clip.height // 2))
        return clip.copy(width=clip.width // 2, height=clip.height // 2)

    def Super(self, clip, settings):
        return {"clip": clip, "data": b"super"}

    def Analyse(self, *args):
        return {"clip": args[0], "data": b"vectors"}

    def DoubleVectors(self, *args):
        return {"clip": args[0], "data": b"doubled"}


class FakeSvp2:
    def SmoothFps(self, clip, *args):
        import re
        config = args[-1]
        match = re.search(r"rate:\{num:(\d+),den:(\d+),abs:true\}", config)
        if not match:
            raise ValueError("Cannot parse output target FPS")
        return clip.copy(fps=Fraction(int(match.group(1)), int(match.group(2))))


class FakeResize:
    def Bicubic(self, clip, format):
        return clip


class FakeCore:
    def __init__(self):
        self.std = FakeStd(self)
        self.svp1 = FakeSvp1(self)
        self.svp2 = FakeSvp2()
        self.resize = FakeResize()
        self.output = None
        self.borders = []
        self.crops = []
        self.analysis = []
        self.num_threads = 0
        self.max_cache_size = 0


def execute(script, width, height, source_fps=Fraction(24000, 1001), container_fps=None):
    core = FakeCore()
    mock = types.ModuleType("vapoursynth")
    mock.core = core
    mock.YUV = "yuv"
    mock.YUV420P8 = "YUV420P8"
    previous = sys.modules.get("vapoursynth")
    sys.modules["vapoursynth"] = mock
    try:
        runpy.run_path(
            str(MOTION / ("motion-%s.vpy" % script)),
            init_globals={
                "video_in": FakeClip(core, width, height, source_fps),
                "container_fps": source_fps if container_fps is None else container_fps,
            },
        )
    finally:
        if previous is None:
            del sys.modules["vapoursynth"]
        else:
            sys.modules["vapoursynth"] = previous
    return core


class MotionResolutionTests(unittest.TestCase):
    def test_odd_half_width_and_unchanged_full_res(self):
        for fps in (60, 120, 165):
            for width, height in ((426, 240), (854, 480), (640, 360), (1280, 720),
                                  (852, 478), (426, 238)):
                with self.subTest(fps=fps, size=(width, height)):
                    core = execute(fps, width, height)
                    self.assertIsNotNone(core.output)
                    self.assertEqual((core.output.width, core.output.height), (width, height))
                    self.assertEqual(core.output.fps, Fraction(fps, 1))
                    self.assertEqual(len(core.analysis), 1)
                    self.assertEqual(core.analysis[0][0] % 2, 0)
                    self.assertEqual(core.analysis[0][1] % 2, 0)
                    right, bottom = (-width) % 4, (-height) % 4
                    self.assertEqual(core.borders, [(right, bottom)] if (right or bottom) else [])
                    self.assertEqual(core.crops, core.borders)

    def test_missing_container_fps_uses_known_video_node_rate(self):
        for fps in (60, 120, 165):
            core = execute(fps, 426, 240, container_fps=0)
            self.assertEqual(core.output.fps, Fraction(fps, 1))

    def test_unknown_input_fps_is_rejected(self):
        for fps in (60, 120, 165):
            with self.subTest(fps=fps):
                with self.assertRaisesRegex(ValueError, "Source FPS unavailable"):
                    execute(fps, 426, 240, source_fps=Fraction(0, 1), container_fps=0)


if __name__ == "__main__":
    unittest.main()
