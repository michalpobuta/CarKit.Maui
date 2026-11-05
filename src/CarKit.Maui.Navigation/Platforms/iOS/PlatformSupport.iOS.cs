using System.Runtime.Versioning;

// The package has only the net10.0-ios TFM (no Mac Catalyst), and the CarPlay API is iOS only;
// the platform analyzer treats iOS as including Mac Catalyst by default
[assembly: UnsupportedOSPlatform("maccatalyst")]
