# ReleaseGen
ReleaseGen is an opinionated tool for building, packaging, and publishing .NET projects.

Where build settings are set in a Project file ReleaseGen will prioritize using those settings unless passed arguments conflict with them.

## Defaults
### Building
* Build Configuration defaults to ``Release`` unless specified otherwise

### Packaging
Both OS Version Specific and non OS version specific packaging targets are supported

The following are supported packaging targets that are not OS Version Specific:

| Platform | Architectures Supported | Packaging Formats Supported|
|-|-|-|
| Windows | X64, ARM64 | |
| Linux | X64, ARM (32 Bit), ARM64 | |
| macOS | X64 <sup>1</sup>, ARM64 | |

<sup>1</sup> - Note: Apple is discontinuing support for all X64 based macs from macOS 27 and onwards. ReleaseGen intends to support x86-64 based Macs for as long as the .NET SDK supports it.

For OS Version Specific Targets, the same principles apply except that the OS version will be added to the target framework and only versions equal to or newer than the required version will be able to run the executables.

## What ReleaseGen isn't
* A changelog or release notes generator/formatter
* A test coverage checker


## Alternatives
* [dotnet-releaser](https://github.com/xoofx/dotnet-releaser)
