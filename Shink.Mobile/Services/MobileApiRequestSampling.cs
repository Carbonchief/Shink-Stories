namespace Shink.Mobile.Services;

internal static class MobileApiRequestSampling
{
    public const double RoutineSuccessRate = 0.10;
    public const double SlowRequestThresholdMilliseconds = 2_000;

    public static double SampleRate(bool isSuccess, double durationMilliseconds) =>
        !isSuccess || durationMilliseconds >= SlowRequestThresholdMilliseconds ? 1 : RoutineSuccessRate;

    public static bool ShouldCapture(bool isSuccess, double durationMilliseconds, double randomDraw) =>
        SampleRate(isSuccess, durationMilliseconds) == 1 || randomDraw < RoutineSuccessRate;
}
