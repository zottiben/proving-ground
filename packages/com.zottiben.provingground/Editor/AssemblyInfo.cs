using System.Runtime.CompilerServices;

// Version comparison and release-payload parsing are implementation details rather than
// API, but they decide whether every user sees an update banner, so they are worth
// testing directly instead of promoting to public.
[assembly: InternalsVisibleTo("ProvingGround.Tests.Editor")]
