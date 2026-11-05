namespace CarKit.Maui;

// A template variant with its own version requirement ("variants" in Rules/*.json). ICarCapabilities.Supports stays per
// template type; the validator checks the variant when choosing a fallback (spec: "Platform template decisions (Stage
// 4)").
internal static class CarTemplateVariants
{
    // QRCodeSignInMethod has @RequiresCarApi(4) in the Car App Library, the rest of sign-in API 2
    public const string QrCode = "QrCode";

    public static string? Of(CarTemplate template) => template switch
    {
        AndroidAutoSignInTemplate { Method: AndroidAutoQrCodeSignIn } => QrCode,
        _ => null,
    };
}
