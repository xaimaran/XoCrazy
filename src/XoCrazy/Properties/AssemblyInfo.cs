using System.Reflection;
using System.Runtime.InteropServices;

// Product name only. The assembly *name* is XoCrazy.dll: it is baked into the .pkgdef,
// the VSIX asset path and the installed extension folder, and renaming it turns an update into
// a fresh install with the old copy left registered alongside it.
[assembly: AssemblyTitle("XoCrazy")]
[assembly: AssemblyDescription("Live editor color editing for Visual Studio.")]
[assembly: AssemblyProduct("XoCrazy")]

// Must match the VSIX manifest Publisher, which must in turn match the Marketplace publisher
// display name — the upload is rejected outright when the two disagree.
[assembly: AssemblyCompany("Xaimaran")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: ComVisible(false)]