using System.Runtime.CompilerServices;

// Unity's test assembly and the headless dotnet test assembly have different
// names; both need internals so tests can reach state without widening the API.
[assembly: InternalsVisibleTo("Thesis.Tests.EditMode")]
[assembly: InternalsVisibleTo("Thesis.Tests")]
