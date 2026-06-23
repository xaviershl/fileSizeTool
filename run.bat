@echo off
rem 简单的运行时检测启动脚本：如果没有安装 dotnet 则弹窗提示用户下载安装
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    powershell -Command "Add-Type -AssemblyName PresentationFramework;[System.Windows.MessageBox]::Show('未检测到 .NET 运行时。\n\n请安装 .NET 8 运行时：\nhttps://dotnet.microsoft.com/download/dotnet/8.0','缺少运行时')"
    exit /b 1
)

rem 如果检测到 dotnet，则使用框架依赖方式运行程序集
dotnet "%~dp0FileSizeTool.dll"
