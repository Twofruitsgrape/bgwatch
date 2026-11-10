; ---------------------------------------------------------------
;  bgwatch 安装脚本（NSIS 3.x）
;
;  和 Obsidian / 7-Zip / VLC / Discord 用的是同一个安装引擎。
;  编译：makensis installer.nsi
; ---------------------------------------------------------------

Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 32

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define APPNAME     "bgwatch"
!define APPEXE      "bgwatch.exe"
!define APPVERSION  "1.0.0"
!define PUBLISHER   "bgwatch"
!define APPDESC     "谁在后台自己跑起来 — 一屏看完，一行关掉，一键还原"
!define REGKEY      "Software\bgwatch"
!define UNINSTKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\bgwatch"
!define DOTNET_URL  "https://dotnet.microsoft.com/download/dotnet/10.0/runtime"

!define ROOT "${__FILEDIR__}\.."

Name "${APPNAME}"
BrandingText "${APPNAME} ${APPVERSION}"
OutFile "${ROOT}\dist\bgwatch-${APPVERSION}-setup.exe"

; 装到用户目录：不要管理员、不弹 UAC。VS Code / Obsidian / Discord 都是这么做的。
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "${REGKEY}" "InstallDir"
RequestExecutionLevel user

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName"     "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPDESC}"
VIAddVersionKey "FileVersion"     "1.0.0.0"
VIAddVersionKey "ProductVersion"  "${APPVERSION}"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "MIT License"

; ---------------------------------------------------------------- 外观
!define MUI_ICON   "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 ${APPNAME}"
!define MUI_WELCOMEPAGE_TEXT  "${APPDESC}$\r$\n$\r$\n它会把四类「会自己启动」的东西列出来：服务、登录启动项、计划任务、启动文件夹。$\r$\n$\r$\n默认开启安全模式，只显示应用类项目，Windows 系统 / 驱动 / 安全软件默认隐藏 —— 不会被误关。$\r$\n$\r$\n只做可逆的改动：每次修改前先把原值存下来，随时能一键还原。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"
!define MUI_FINISHPAGE_TEXT "${APPNAME} 已安装到：$\r$\n$INSTDIR$\r$\n$\r$\n修改服务或「所有用户」的启动项时，程序会提示你切换成管理员。$\r$\n$\r$\n卸载不会删除你的改动记录（%LOCALAPPDATA%\bgwatch），以免丢掉还原能力。"
!define MUI_DIRECTORYPAGE_TEXT_TOP "安装到你的用户目录，不需要管理员权限。"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

; ---------------------------------------------------------------- 安装前检查
Function .onInit
  ; 需要 .NET 10 桌面运行时。没有就告诉用户去哪下，而不是装完打不开。
  IfFileExists "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*" dotnet_ok
    MessageBox MB_YESNO|MB_ICONEXCLAMATION \
      "${APPNAME} 需要 .NET 10 桌面运行时（x64），这台机器上没检测到。$\r$\n$\r$\n点「是」打开下载页面，装完再运行本安装程序；点「否」退出。" \
      IDYES open_dotnet
    Abort
  open_dotnet:
    ExecShell "open" "${DOTNET_URL}"
    Abort
  dotnet_ok:
FunctionEnd

; ---------------------------------------------------------------- 安装
Section "bgwatch 主程序（必需）" SecMain
  SectionIn RO
  SetShellVarContext current

  SetOutPath "$INSTDIR"
  File "${ROOT}\dist\${APPEXE}"
  File "app.ico"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
  CreateShortcut "$SMPROGRAMS\${APPNAME}\卸载 ${APPNAME}.lnk" "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "${REGKEY}" "InstallDir" "$INSTDIR"

  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayVersion"  "${APPVERSION}"
  WriteRegStr   HKCU "${UNINSTKEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayIcon"     "$INSTDIR\${APPEXE}"
  WriteRegStr   HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr   HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "桌面快捷方式" SecDesktop
  SetShellVarContext current
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    "程序本体、开始菜单快捷方式和卸载程序。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "在桌面放一个快捷方式。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---------------------------------------------------------------- 卸载
Section "Uninstall"
  SetShellVarContext current

  ; 还有没还原的改动？先提醒，别让人以为卸了就干净了。
  ClearErrors
  FileOpen $0 "$LOCALAPPDATA\bgwatch\undo.json" r
  ${IfNot} ${Errors}
    FileRead $0 $1
    FileClose $0
    ${If} $1 != "[]"
    ${AndIf} $1 != ""
      MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
        "你还有未还原的自启动改动。$\r$\n$\r$\n建议先打开 ${APPNAME} 点「还原全部改动」，再卸载。$\r$\n$\r$\n点「确定」继续卸载（改动记录会保留在 %LOCALAPPDATA%\bgwatch，重装后仍可还原）；点「取消」返回。" \
        IDOK uninst_go
      Abort
    ${EndIf}
  ${EndIf}
  uninst_go:

  Delete "$INSTDIR\${APPEXE}"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\uninstall.exe"
  RMDir  "$INSTDIR"

  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\卸载 ${APPNAME}.lnk"
  RMDir  "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "${REGKEY}"
  ; 故意不删 %LOCALAPPDATA%\bgwatch —— 里面是还原记录和隔离的启动项文件
SectionEnd
