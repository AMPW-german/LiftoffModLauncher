dotnet publish -c Release -r win-x64 -p:AssemblyName=LiftoffModLauncher-win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r linux-x64 -p:AssemblyName=LiftoffModLauncher-linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r osx-x64 -p:AssemblyName=LiftoffModLauncher-osx-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r osx-arm64 -p:AssemblyName=LiftoffModLauncher-osx-arm64 --self-contained true -p:PublishSingleFile=true