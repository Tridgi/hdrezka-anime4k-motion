import os
import vapoursynth as vs
core = vs.core
core.num_threads = 4
core.max_cache_size = 384
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
for name in ("svpflow1_vs.dll", "svpflow2_vs.dll"):
    core.std.LoadPlugin(os.path.join(root, "runtime", "open-svpflow", name))
# mpv's streamed source intentionally advertises variable FPS (0/1).
# Give SVP a rational rate; otherwise its VFR fallback expects an absent fps prop.
from fractions import Fraction
if not (0 < container_fps <= 240):
    raise ValueError("Source FPS unavailable: motion interpolation requires a known rate")
rate = Fraction(str(container_fps)).limit_denominator(100000)
clip = core.std.AssumeFPS(video_in, fpsnum=rate.numerator, fpsden=rate.denominator)
if clip.format.color_family != vs.YUV or clip.format.subsampling_w != 1 or clip.format.subsampling_h != 1:
    clip = core.resize.Bicubic(clip, format=vs.YUV420P8)
analysis_clip = core.svp1.Halve(clip)
sup = core.svp1.Super(analysis_clip, "{pel:1,gpu:1}")
vectors = core.svp1.Analyse(sup["clip"], sup["data"], analysis_clip,
    "{block:{w:16,overlap:1},main:{search:{coarse:{distance:-1,trymany:false}}}}")
vectors = core.svp1.DoubleVectors(vectors["clip"], vectors["data"])
sup = core.svp1.Super(clip, "{pel:1,gpu:1}")
result = core.svp2.SmoothFps(clip, sup["clip"], sup["data"], vectors["clip"], vectors["data"],
    "{rate:{num:120,den:1,abs:true},algo:23,mask:{cover:80,area:0},scene:{blend:true,mode:0}}")
result.set_output()
