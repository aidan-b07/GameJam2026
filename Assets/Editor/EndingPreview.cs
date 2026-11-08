using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Test helpers for the dawn ending (DawnEnding), called over MCP in play mode:
// Setup puts both players just before their clock doors and pauses the
// Editor with a fixed step; Step advances it; Capture renders both lanes and
// the screen-space overlays (rift, seam, letterbox, titles) into one PNG.
public static class EndingPreview
{
    public static float MaxMs, SumMs;
    public static int Frames;

    // Players 3 units before their door's trigger; ScreenLeash off; paused, 0.02 s steps.
    public static string Setup(float beforeTrigger = 3f)
    {
        if (!Application.isPlaying) return "not playing";
        var rig = Object.FindAnyObjectByType<CoopCamera>();
        var leash = rig != null ? rig.GetComponent<ScreenLeash>() : null;
        if (leash != null) leash.enabled = false;
        var doors = Object.FindObjectsByType<ClockDoor>().OrderBy(d => d.lane).ToArray();
        var flow = GameFlow.Instance;
        foreach (var d in doors) Teleport(flow.players[d.lane], d.triggerX - beforeTrigger);
        EditorApplication.isPaused = true;
        Time.captureDeltaTime = 0.02f;
        MaxMs = SumMs = 0f; Frames = 0;
        return string.Join(", ", doors.Select(d => $"door {d.lane} trigger {d.triggerX} mark {d.markX}"));
    }

    public static void Teleport(Transform p, float x)
    {
        var rb = p.GetComponent<Rigidbody2D>();
        var pos = new Vector2(x, rb.position.y + 0.2f);
        rb.position = pos;
        p.position = new Vector3(pos.x, pos.y, p.position.z);
        rb.linearVelocity = Vector2.zero;
    }

    // Moves both players over their trigger (starts the ending once both are there).
    public static void Arrive(float past = 0.2f)
    {
        var flow = GameFlow.Instance;
        foreach (var d in Object.FindObjectsByType<ClockDoor>()) Teleport(flow.players[d.lane], d.triggerX + past);
    }

    public static string Step(int n)
    {
        var ending = Object.FindAnyObjectByType<DawnEnding>();
        for (int i = 0; i < n; i++)
        {
            EditorApplication.Step();
            if (ending != null && ending.Running && ending.sky != null)
            {
                MaxMs = Mathf.Max(MaxMs, ending.sky.LastMs);
                SumMs += ending.sky.LastMs;
                Frames++;
            }
        }
        return Status();
    }

    public static string Status()
    {
        var ending = Object.FindAnyObjectByType<DawnEnding>();
        var gm = GameManager.INSTANCE;
        var doors = Object.FindObjectsByType<ClockDoor>().OrderBy(d => d.lane).ToArray();
        string s = ending == null ? "no ending" : $"running {ending.Running} t {ending.T:F2} peek {ending.sky?.PeekTime:F2} ms last {ending.sky?.LastMs:F2} max {MaxMs:F2} avg {(Frames > 0 ? SumMs / Frames : 0):F2}";
        if (gm != null) s += $" hours {gm.GetLaneHour(0):F2}/{gm.GetLaneHour(1):F2}";
        s += " doors " + string.Join("/", doors.Select(d => d.Current));
        var flow = GameFlow.Instance;
        if (flow != null) s += " players " + string.Join("/", flow.players.Select(p => p.position.x.ToString("F2")));
        return s;
    }

    // Both lanes (Tick on top) plus the overlay canvases, 1280x720, to path.
    public static string Capture(string path)
    {
        var cams = Object.FindAnyObjectByType<CoopCamera>().cameras;
        const int W = 1280, H = 720;
        var result = new Color[W * H];
        for (int lane = 0; lane < cams.Length; lane++)
        {
            var cam = cams[lane];
            var rt = RenderTexture.GetTemporary(W, H / 2, 24, RenderTextureFormat.ARGB32);
            var rect = cam.rect;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            cam.rect = rect;
            var px = Read(rt);
            RenderTexture.ReleaseTemporary(rt);
            int y0 = lane == 0 ? H / 2 : 0;   // texture rows bottom-up: Tick on top
            for (int y = 0; y < H / 2; y++)
                System.Array.Copy(px, y * W, result, (y0 + y) * W, W);
        }

        // Overlays: render each screen-space canvas with a camera far from the level.
        var uiGo = new GameObject("CaptureUI");
        uiGo.transform.position = new Vector3(10000f, 10000f, -10f);
        var ui = uiGo.AddComponent<Camera>();
        ui.orthographic = true;
        ui.clearFlags = CameraClearFlags.SolidColor;
        ui.backgroundColor = new Color(0, 0, 0, 0);
        var urt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        ui.targetTexture = urt;
        var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled)
            .OrderBy(c => c.sortingOrder).ToArray();
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ui; c.planeDistance = 1f; }
        Canvas.ForceUpdateCanvases();
        ui.Render();
        var upx = Read(urt);
        foreach (var c in canvases) c.renderMode = RenderMode.ScreenSpaceOverlay;
        ui.targetTexture = null;
        RenderTexture.ReleaseTemporary(urt);
        Object.DestroyImmediate(uiGo);
        for (int i = 0; i < result.Length; i++)
        {
            var u = upx[i];
            float a = Mathf.Sqrt(Mathf.Clamp01(u.a));
            var w = result[i];
            result[i] = new Color(u.r + w.r * (1 - a), u.g + w.g * (1 - a), u.b + w.b * (1 - a), 1f);
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels(result);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return path;
    }

    static Color[] Read(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var px = tex.GetPixels();
        Object.DestroyImmediate(tex);
        return px;
    }
}
