using Microsoft.Extensions.Options;

namespace GpsdExporter.Connection;

/// <summary>Validates <see cref="GpsdOptions"/> for the options pattern. Source-generated, so it is AOT-safe.</summary>
[OptionsValidator]
public sealed partial class GpsdOptionsValidator : IValidateOptions<GpsdOptions>;
