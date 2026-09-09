using System;

namespace SaltExtraDebug;

internal static class DebugViewMath
{
    internal static float TileCenter(int tileCoordinate)
    {
        // Turf centers run from tile*100-50 through tile*100+49.
        return tileCoordinate * 100f - 0.5f;
    }

    internal static float FitOrthographicSize(float width, float height, float aspect)
    {
        if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f)
        {
            aspect = 1f;
        }
        const float margin = 2f;
        return Math.Max(height * 0.5f + margin, (width * 0.5f + margin) / aspect);
    }

    internal static float WorldHeightForPixels(float pixels, float orthographicSize, int pixelHeight)
    {
        return pixels * (2f * orthographicSize) / Math.Max(1, pixelHeight);
    }
}
