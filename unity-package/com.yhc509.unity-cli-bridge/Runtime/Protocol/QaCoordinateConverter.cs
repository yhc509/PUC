namespace UnityCli.Protocol
{
    public static class QaCoordinateConverter
    {
        public static int ConvertScreenshotXToScreenX(int rawX, int screenWidth, int screenshotWidth)
        {
            return ScaleCoordinate(rawX, screenWidth, screenshotWidth);
        }

        public static int ConvertScreenXToScreenshotX(int screenX, int screenWidth, int screenshotWidth)
        {
            return ScaleCoordinate(screenX, screenshotWidth, screenWidth);
        }

        public static int ConvertScreenshotYToScreenY(int rawY, int screenHeight, int screenshotHeight)
        {
            if (screenshotHeight <= 0 || screenHeight <= 0)
            {
                return rawY;
            }

            int scaledY = ScaleCoordinate(rawY, screenHeight, screenshotHeight);
            return screenHeight - scaledY;
        }

        public static int ConvertScreenYToScreenshotY(int screenY, int screenHeight, int screenshotHeight)
        {
            if (screenHeight <= 0 || screenshotHeight <= 0)
            {
                return screenY;
            }

            return ScaleCoordinate(screenHeight - screenY, screenshotHeight, screenHeight);
        }

        public static bool IsAspectMismatch(int screenshotWidth, int screenshotHeight, int screenWidth, int screenHeight)
        {
            if (screenshotWidth <= 0 || screenshotHeight <= 0 || screenWidth <= 0 || screenHeight <= 0)
            {
                return false;
            }

            double screenshotAspect = (double)screenshotWidth / screenshotHeight;
            double screenAspect = (double)screenWidth / screenHeight;
            double relativeDiff = System.Math.Abs(screenshotAspect - screenAspect) / screenAspect;
            return relativeDiff > 0.05;
        }

        private static int ScaleCoordinate(int rawValue, int screenSize, int screenshotSize)
        {
            if (screenshotSize <= 0 || screenSize <= 0)
            {
                return rawValue;
            }

            long scaledValue = (long)rawValue * screenSize / screenshotSize;
            if (scaledValue > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (scaledValue < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)scaledValue;
        }
    }
}
