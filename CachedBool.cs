namespace MessageHeightTwitch;

/// <summary>
/// A lazily computed flag in a shared lookup table. <see cref="Unknown"/> until some thread computes it.
/// </summary>
internal enum CachedBool : byte
{
    Unknown,
    False,
    True
}
