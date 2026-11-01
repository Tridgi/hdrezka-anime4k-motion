import os
import vapoursynth as vs
core = vs.core
core.num_threads = 4
core.max_cache_size = 384
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
for name in ("svpflow1_vs.dll", "svpflow2_vs.dll"):
    core.std.LoadPlugin(os.path.join(root, "runtime", "open-svpflow", name))
# Prefer the container's explicit frame rate. Some lower-quality streams
# lack it; fall back only to a valid rate carried by the incoming VideoNode.
# Never assume a hard-coded 24/30 FPS for a stream with unknown timing.
from fractions import Fraction
rate = Fraction(str(container_fps)).limit_denominator(100000) if 0 < container_fps <= 240 else video_in.fps
if rate.numerator <= 0 or rate.denominator <= 0 or rate > 240:
    raise ValueError("Source FPS unavailable: motion interpolation requires a known rate")
clip = core.std.AssumeFPS(video_in, fpsnum=rate.numerator, fpsden=rate.denominator)
if clip.format.color_family != vs.YUV or clip.format.subsampling_w != 1 or clip.format.subsampling_h != 1:
    clip = core.resize.Bicubic(clip, format=vs.YUV420P8)
# SVPflow Halve needs an even-sized YUV420 result. A 426px or 854px
# stream halves to an odd width (213/427). Pad the analysis input to
# multiples of four, then crop the generated frames back to the
# original visible size. No scaling/stretching of the picture.
pad_right = (-clip.width) % 4
pad_bottom = (-clip.height) % 4
if pad_right or pad_bottom:
    clip = core.std.AddBorders(clip, right=pad_right, bottom=pad_bottom)
analysis_clip = core.svp1.Halve(clip)
sup = core.svp1.Super(analysis_clip, "{pel:1,gpu:1}")
vectors = core.svp1.Analyse(sup["clip"], sup["data"], analysis_clip,
    "{block:{w:16,overlap:1},main:{search:{coarse:{distance:-1,trymany:false}}}}")
vectors = core.svp1.DoubleVectors(vectors["clip"], vectors["data"])
sup = core.svp1.Super(clip, "{pel:1,gpu:1}")
result = core.svp2.SmoothFps(clip, sup["clip"], sup["data"], vectors["clip"], vectors["data"],
    "{rate:{num:60,den:1,abs:true},algo:23,mask:{cover:80,area:0},scene:{blend:true,mode:0}}")
if pad_right or pad_bottom:
    result = core.std.CropRel(result, right=pad_right, bottom=pad_bottom)
result.set_output()
