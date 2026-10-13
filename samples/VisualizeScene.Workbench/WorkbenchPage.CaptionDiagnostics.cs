namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void PublishCaptionSizing()
    {
#if __WASM__
        if (!DiagnosticsEnabled) return;
        var result = graph.Session.LastCaptionSizing;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.visualizeSceneCaptionSizing={{measurements:{graph.CaptionMeasurementBuilds},cached:{graph.CachedCaptionMeasurements},eligible:{result?.EligibleNodes ?? 0},resized:{result?.ResizedNodes ?? 0},measured:{result?.MeasuredNodes ?? 0},examined:{result?.ExaminedNodes ?? 0}}};");
#endif
    }
}
