# Performance comparison

For the commands to work I expect you to have a Powershell session in the root
of the repo.

## Non-AOT

```pwsh
dotnet build my-cli
dotnet run --project my-cli --no-build --no-restore -- up
```

**This takes about 10s.**

## AOT

I modified the project to perform AOT & trimming:

```xml
<PublishAot>true</PublishAot>
<PublishTrimmed>true</PublishTrimmed>
```

The idea being that I wanted to eliminate any overhead due .NET startup.

```pwsh
dotnet publish my-cli -r win-x64 -c Release
my-cli\bin\Release\net10.0\win-x64\publish\my-cli.exe up
```

**This also takes about 10s**

While the equivalent with the plain Aspire CLI:

## Aspire CLI

The custom CLI invokes Aspire twice:

* Once to get the status of the app host
* Once to perform the up/down operation

So for a baseline, let's just invoke them from Powershell:

```pwsh
aspire ps --format json ; aspire resource webapp start --apphost "aspire-cli-test.AppHost\aspire-cli-test.AppHost.csproj"
```

**This completes in < 1s**

## Redirection

I have even tried to turn off output redirection (`DISABLE_ASPIRE_OUTPUT` at the
top of `Program.cs`). No measurable improvement.

## Aspire Logging

Normally I also have a third Aspire call for streaming the logs, which is
disabled (`DISABLE_ASPIRE_LOGGING` at the top of `Program.cs`). 

## Linux vs Windows

I was hoping that this is primarily an issue on Windows, but it's not. I was
able to reproduce a similar ratio of 10x slower when running in WSL (Arch) as
well as Linux on physical machine (CentOS).

## What do we do about it?

There, there probably isn't much we can do about that because being about 10x
slower is apparently what can be expected when comparing terminal invocations
against `Process.Start()`. This [blog post][process-start-blog] gives a good
overview of why.

We could:

1. Use the underlying JsonRPC API that the CLI uses to talk to the DCP. I don't
   believe this to be a publicly documented API.

2. Ask for the official Aspire CLI to support streaming command like where we
   can use stdin to send new commands. This way, we'd only pay the overhead
   penalty once.

3. Don't use a managed CLI but rather use shell scripting or just use the aspire
   CLI directly.

4. Live with it.

[process-start-blog]: https://www.codegenes.net/blog/process-start-significantly-slower-than-executing-in-console/
