using UnityEngine;

/// <summary>
/// Anti-aliased 2D software rasterizer used for generating crisp procedural sprites and menu textures.
/// </summary>
public class Canvas2D
{
    public readonly int width;
    public readonly int height;
    public readonly Color32[] pixels;

    public Canvas2D(int w, int h)
    {
        width = w;
        height = h;
        pixels = new Color32[w * h];
    }

    public Texture2D ToTexture2D()
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    public void BlendPixel(int x, int y, Color32 src)
    {
        if (x < 0 || x >= width || y < 0 || y >= height || src.a == 0) return;
        int idx = y * width + x;
        Color32 dst = pixels[idx];

        if (dst.a == 0)
        {
            pixels[idx] = src;
            return;
        }

        float srcA = src.a / 255f;
        float dstA = dst.a / 255f;
        float outA = srcA + dstA * (1f - srcA);

        if (outA <= 0.001f)
        {
            pixels[idx] = new Color32(0, 0, 0, 0);
            return;
        }

        byte r = (byte)Mathf.Clamp(((src.r * srcA) + (dst.r * dstA * (1f - srcA))) / outA, 0f, 255f);
        byte g = (byte)Mathf.Clamp(((src.g * srcA) + (dst.g * dstA * (1f - srcA))) / outA, 0f, 255f);
        byte b = (byte)Mathf.Clamp(((src.b * srcA) + (dst.b * dstA * (1f - srcA))) / outA, 0f, 255f);
        byte a = (byte)Mathf.Clamp(outA * 255f, 0f, 255f);

        pixels[idx] = new Color32(r, g, b, a);
    }

    public void DrawCircle(float cx, float cy, float radius, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        float totalR = radius + outline;
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - totalR - 2));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + totalR + 2));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - totalR - 2));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + totalR + 2));

        for (int y = minY; y <= maxY; y++)
        {
            float dy = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > totalR + 0.5f) continue;

                if (outline > 0f)
                {
                    if (dist <= radius - 0.5f)
                    {
                        BlendPixel(x, y, fill);
                    }
                    else if (dist <= radius + 0.5f)
                    {
                        float t = dist - (radius - 0.5f);
                        Color32 c = Color32.Lerp(fill, outlineCol, t);
                        BlendPixel(x, y, c);
                    }
                    else if (dist <= totalR - 0.5f)
                    {
                        BlendPixel(x, y, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(totalR + 0.5f - dist);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(x, y, c);
                }
            }
        }
    }

    public void DrawEllipse(float cx, float cy, float rx, float ry, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        DrawRotatedEllipse(cx, cy, rx, ry, 0f, fill, outline, outlineCol);
    }

    public void DrawRotatedEllipse(float cx, float cy, float rx, float ry, float angleDeg, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        float maxR = Mathf.Max(rx, ry) + outline + 2f;
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - maxR));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + maxR));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - maxR));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + maxR));

        float rad = -angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        float invRx = 1f / Mathf.Max(0.1f, rx);
        float invRy = 1f / Mathf.Max(0.1f, ry);
        float minDim = Mathf.Min(rx, ry);

        for (int y = minY; y <= maxY; y++)
        {
            float dy0 = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                float dx0 = x - cx;

                float lx = dx0 * cos - dy0 * sin;
                float ly = dx0 * sin + dy0 * cos;

                float nx = lx * invRx;
                float ny = ly * invRy;
                float normDist = Mathf.Sqrt(nx * nx + ny * ny);

                float pixelDist = (normDist - 1.0f) * minDim;
                float totalOutline = outline;

                if (pixelDist > totalOutline + 0.5f) continue;

                if (outline > 0f)
                {
                    if (pixelDist <= -0.5f)
                    {
                        BlendPixel(x, y, fill);
                    }
                    else if (pixelDist <= 0.5f)
                    {
                        float t = pixelDist + 0.5f;
                        Color32 c = Color32.Lerp(fill, outlineCol, t);
                        BlendPixel(x, y, c);
                    }
                    else if (pixelDist <= totalOutline - 0.5f)
                    {
                        BlendPixel(x, y, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(totalOutline + 0.5f - pixelDist);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(0.5f - pixelDist);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(x, y, c);
                }
            }
        }
    }

    public void DrawCapsule(float x1, float y1, float x2, float y2, float radius, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        float totalR = radius + outline;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x1, x2) - totalR - 2));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(x1, x2) + totalR + 2));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y1, y2) - totalR - 2));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(y1, y2) + totalR + 2));

        Vector2 a = new Vector2(x1, y1);
        Vector2 b = new Vector2(x2, y2);
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x, y);
                float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
                Vector2 closest = a + ab * t;
                float dist = Vector2.Distance(p, closest);

                if (dist > totalR + 0.5f) continue;

                if (outline > 0f)
                {
                    if (dist <= radius - 0.5f)
                    {
                        BlendPixel(x, y, fill);
                    }
                    else if (dist <= radius + 0.5f)
                    {
                        float blend = dist - (radius - 0.5f);
                        Color32 c = Color32.Lerp(fill, outlineCol, blend);
                        BlendPixel(x, y, c);
                    }
                    else if (dist <= totalR - 0.5f)
                    {
                        BlendPixel(x, y, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(totalR + 0.5f - dist);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(x, y, c);
                }
            }
        }
    }

    public void DrawRect(float x, float y, float w, float h, Color32 fill, float cornerRadius = 0f, float outline = 0f, Color32 outlineCol = default)
    {
        float totalOutline = outline;
        int minX = Mathf.Max(0, Mathf.FloorToInt(x - totalOutline - 1));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(x + w + totalOutline + 1));
        int minY = Mathf.Max(0, Mathf.FloorToInt(y - totalOutline - 1));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(y + h + totalOutline + 1));

        float r = Mathf.Min(cornerRadius, Mathf.Min(w, h) * 0.5f);
        float cx1 = x + r;
        float cx2 = x + w - r;
        float cy1 = y + r;
        float cy2 = y + h - r;

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float clX = Mathf.Clamp(px, cx1, cx2);
                float clY = Mathf.Clamp(py, cy1, cy2);

                float dx = px - clX;
                float dy = py - clY;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > r + totalOutline + 0.5f) continue;

                if (outline > 0f)
                {
                    if (dist <= r - 0.5f)
                    {
                        BlendPixel(px, py, fill);
                    }
                    else if (dist <= r + 0.5f)
                    {
                        float t = dist - (r - 0.5f);
                        BlendPixel(px, py, Color32.Lerp(fill, outlineCol, t));
                    }
                    else if (dist <= r + totalOutline - 0.5f)
                    {
                        BlendPixel(px, py, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(r + totalOutline + 0.5f - dist);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(px, py, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(r + 0.5f - dist);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(px, py, c);
                }
            }
        }
    }

    public void DrawVerticalGradientRect(float x, float y, float w, float h, Color32 topColor, Color32 botColor, float cornerRadius = 0f, float outline = 0f, Color32 outlineCol = default)
    {
        float totalOutline = outline;
        int minX = Mathf.Max(0, Mathf.FloorToInt(x - totalOutline - 1));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(x + w + totalOutline + 1));
        int minY = Mathf.Max(0, Mathf.FloorToInt(y - totalOutline - 1));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(y + h + totalOutline + 1));

        float r = Mathf.Min(cornerRadius, Mathf.Min(w, h) * 0.5f);
        float cx1 = x + r;
        float cx2 = x + w - r;
        float cy1 = y + r;
        float cy2 = y + h - r;

        for (int py = minY; py <= maxY; py++)
        {
            float tGrad = Mathf.Clamp01((py - y) / Mathf.Max(1f, h));
            Color32 fill = Color32.Lerp(botColor, topColor, tGrad);

            for (int px = minX; px <= maxX; px++)
            {
                float clX = Mathf.Clamp(px, cx1, cx2);
                float clY = Mathf.Clamp(py, cy1, cy2);

                float dx = px - clX;
                float dy = py - clY;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > r + totalOutline + 0.5f) continue;

                if (outline > 0f)
                {
                    if (dist <= r - 0.5f)
                    {
                        BlendPixel(px, py, fill);
                    }
                    else if (dist <= r + 0.5f)
                    {
                        float t = dist - (r - 0.5f);
                        BlendPixel(px, py, Color32.Lerp(fill, outlineCol, t));
                    }
                    else if (dist <= r + totalOutline - 0.5f)
                    {
                        BlendPixel(px, py, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(r + totalOutline + 0.5f - dist);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(px, py, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(r + 0.5f - dist);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(px, py, c);
                }
            }
        }
    }

    public void DrawCrescent(float cx, float cy, float rOuter, float rInner, float cutOffsetX, float cutOffsetY, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        float cutCx = cx + cutOffsetX;
        float cutCy = cy + cutOffsetY;

        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rOuter - outline - 2));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + rOuter + outline + 2));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - rOuter - outline - 2));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + rOuter + outline + 2));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dOuter = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float dInner = Vector2.Distance(new Vector2(x, y), new Vector2(cutCx, cutCy));

                float distOuterEdge = dOuter - rOuter;
                float distInnerEdge = rInner - dInner;
                float sdf = Mathf.Max(distOuterEdge, distInnerEdge);

                if (sdf > outline + 0.5f) continue;

                if (outline > 0f)
                {
                    if (sdf <= -0.5f)
                    {
                        BlendPixel(x, y, fill);
                    }
                    else if (sdf <= 0.5f)
                    {
                        float t = sdf + 0.5f;
                        BlendPixel(x, y, Color32.Lerp(fill, outlineCol, t));
                    }
                    else if (sdf <= outline - 0.5f)
                    {
                        BlendPixel(x, y, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(outline + 0.5f - sdf);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(0.5f - sdf);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(x, y, c);
                }
            }
        }
    }

    public void DrawTriangle(Vector2 p1, Vector2 p2, Vector2 p3, Color32 fill, float outline = 0f, Color32 outlineCol = default)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)) - outline - 2));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)) + outline + 2));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)) - outline - 2));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)) + outline + 2));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x, y);

                float d1 = Sign(p, p1, p2);
                float d2 = Sign(p, p2, p3);
                float d3 = Sign(p, p3, p1);

                bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
                bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
                bool inside = !(hasNeg && hasPos);

                float distToEdge = Mathf.Min(
                    DistToSegment(p, p1, p2),
                    Mathf.Min(DistToSegment(p, p2, p3), DistToSegment(p, p3, p1))
                );

                float sdf = inside ? -distToEdge : distToEdge;

                if (sdf > outline + 0.5f) continue;

                if (outline > 0f)
                {
                    if (sdf <= -0.5f)
                    {
                        BlendPixel(x, y, fill);
                    }
                    else if (sdf <= 0.5f)
                    {
                        float t = sdf + 0.5f;
                        BlendPixel(x, y, Color32.Lerp(fill, outlineCol, t));
                    }
                    else if (sdf <= outline - 0.5f)
                    {
                        BlendPixel(x, y, outlineCol);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(outline + 0.5f - sdf);
                        Color32 c = outlineCol;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
                else
                {
                    float alpha = Mathf.Clamp01(0.5f - sdf);
                    Color32 c = fill;
                    c.a = (byte)(c.a * alpha);
                    BlendPixel(x, y, c);
                }
            }
        }
    }

    public void DrawStar(float cx, float cy, float rInner, float rOuter, int points, Color32 color)
    {
        Vector2[] verts = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float r = (i % 2 == 0) ? rOuter : rInner;
            float ang = i * (Mathf.PI / points);
            verts[i] = new Vector2(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
        }

        Vector2 center = new Vector2(cx, cy);
        for (int i = 0; i < points * 2; i++)
        {
            Vector2 pNext = verts[(i + 1) % (points * 2)];
            DrawTriangle(center, verts[i], pNext, color);
        }
    }

    public void DrawGlow(float cx, float cy, float radius, Color32 glowColor)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + radius));

        for (int y = minY; y <= maxY; y++)
        {
            float dy = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist >= radius) continue;

                float falloff = Mathf.Pow(Mathf.Clamp01(1.0f - (dist / radius)), 2.2f);
                Color32 c = glowColor;
                c.a = (byte)(glowColor.a * falloff);
                BlendPixel(x, y, c);
            }
        }
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(p, a + ab * t);
    }
}
