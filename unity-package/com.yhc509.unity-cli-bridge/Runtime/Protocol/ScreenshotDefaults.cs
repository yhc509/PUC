using System;

namespace UnityCli.Protocol
{
    /// <summary>
    /// Encoding and downscale defaults for <c>screenshot</c>.
    ///
    /// A screenshot is the heaviest single response an agent can ask for: a 1920x1080 PNG bills at
    /// roughly 2,000 image tokens, so a handful of captures costs more than every text response in
    /// the session combined. The defaults here trade a lossless full-resolution image — which almost
    /// no caller needs and no vision model can use at full detail anyway — for a JPEG capped at
    /// <see cref="DefaultMaxWidth"/>, which is still wide enough to read UI text.
    ///
    /// Every knob stays overridable: <c>--format png</c> restores lossless, <c>--width</c>/
    /// <c>--height</c> take precedence over the cap, and <c>--max-width 0</c> turns the cap off
    /// while leaving the size auto-detected.
    ///
    /// Kept free of Unity types so both the CLI and the bridge resolve the defaults from one place,
    /// and so the resolution is unit-testable.
    /// </summary>
    public static class ScreenshotDefaults
    {
        public const string FormatPng = "png";
        public const string FormatJpg = "jpg";

        /// <summary>Visually lossless enough for UI verification at a fraction of the PNG size.</summary>
        public const int DefaultJpegQuality = 75;

        /// <summary>
        /// Wide enough to keep UI text legible, narrow enough that a 16:9 capture bills at roughly
        /// 576 image tokens instead of 2,040.
        /// </summary>
        public const int DefaultMaxWidth = 1024;

        /// <summary>
        /// Wire sentinel for "the caller explicitly asked for no downscale cap". The wire model
        /// cannot use <c>Nullable&lt;int&gt;</c> (Unity's JsonUtility drops it), and <c>0</c> already
        /// means "unspecified", so the opt-out has to be a distinct value.
        /// </summary>
        public const int MaxWidthUncapped = -1;

        /// <summary>
        /// Resolves the encoding to write. An explicit format always wins; otherwise an explicit
        /// output extension decides, because writing JPEG bytes into a file the caller named
        /// <c>.png</c> is a worse outcome than the tokens the default saves.
        /// </summary>
        /// <param name="requestedFormat">Value of <c>--format</c>; null or blank when unspecified.</param>
        /// <param name="outputPath">Value of <c>--path</c>; null or blank when unspecified.</param>
        /// <returns>False when <paramref name="requestedFormat"/> is set but not a known format.</returns>
        public static bool TryResolveFormat(string requestedFormat, string outputPath, out string format)
        {
            if (!IsBlank(requestedFormat))
            {
                return TryNormalizeFormat(requestedFormat, out format);
            }

            format = HasExtension(outputPath, ".png") ? FormatPng : FormatJpg;
            return true;
        }

        /// <summary>Normalizes an explicitly requested format, folding <c>jpeg</c> into <c>jpg</c>.</summary>
        public static bool TryNormalizeFormat(string requestedFormat, out string format)
        {
            string normalized = IsBlank(requestedFormat)
                ? string.Empty
                : requestedFormat.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case FormatPng:
                    format = FormatPng;
                    return true;
                case FormatJpg:
                case "jpeg":
                    format = FormatJpg;
                    return true;
                default:
                    format = FormatPng;
                    return false;
            }
        }

        /// <summary>
        /// Resolves the downscale cap to apply, where <c>0</c> means "do not downscale".
        ///
        /// An explicit <c>--width</c>/<c>--height</c> already states the size the caller wants, so
        /// the cap stays out of its way — that gate predates the default and is why raising it does
        /// not silently shrink sized captures.
        /// </summary>
        /// <param name="requestedMaxWidth">Wire <c>maxWidth</c>: 0 unspecified, negative uncapped.</param>
        public static int ResolveMaxWidth(int requestedMaxWidth, int requestedWidth, int requestedHeight)
        {
            if (requestedMaxWidth < 0)
            {
                return 0;
            }

            if (requestedWidth > 0 || requestedHeight > 0)
            {
                return 0;
            }

            return requestedMaxWidth > 0 ? requestedMaxWidth : DefaultMaxWidth;
        }

        /// <summary>File extension matching the resolved format, dot included.</summary>
        public static string FileExtension(string format)
        {
            return string.Equals(format, FormatJpg, StringComparison.Ordinal) ? ".jpg" : ".png";
        }

        private static bool HasExtension(string path, string extension)
        {
            if (IsBlank(path))
            {
                return false;
            }

            string trimmed = path.Trim();
            return trimmed.Length > extension.Length
                && trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }
    }
}
