@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /optimize /target:winexe /out:DesktopColorSorter.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" Core.cs Personalize.cs StartMenu.cs StartPanel.cs CropDialog.cs PatternAnimator.cs MainForm.cs
