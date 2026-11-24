using Microsoft.Extensions.Options;

namespace GpsdExporter.State;

/// <summary>Validates <see cref="StalenessOptions"/> for the options pattern. Source-generated, so it is AOT-safe.</summary>
[OptionsValidator]
public sealed partial class StalenessOptionsValidator : IValidateOptions<StalenessOptions>;
