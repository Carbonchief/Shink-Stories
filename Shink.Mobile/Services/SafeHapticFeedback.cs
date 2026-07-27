namespace Shink.Mobile.Services;

internal static class SafeHapticFeedback
{
    public static bool TryPerform(HapticFeedbackType feedbackType)
    {
        try
        {
            var hapticFeedback = HapticFeedback.Default;
            if (!hapticFeedback.IsSupported)
            {
                return false;
            }

            hapticFeedback.Perform(feedbackType);
            return true;
        }
        catch
        {
            // Haptics are optional. Some Android vendors report support but still
            // reject a vibration request at runtime, which must not end a tap flow.
            return false;
        }
    }
}
