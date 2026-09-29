rd /s /q bin
rd /s /q obj
:: rd /s /q Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o Release
pause