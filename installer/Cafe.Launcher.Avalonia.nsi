# -*- coding: utf-8 -*-
Unicode true
ManifestDPIAware true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "StrFunc.nsh"
${StrStr}
${UnStrStr}

!ifndef APP_VERSION
  !error "APP_VERSION is required."
!endif
!ifndef FILE_VERSION
  !error "FILE_VERSION is required."
!endif
!ifndef PUBLISH_GLOB
  !error "PUBLISH_GLOB is required."
!endif
!ifndef UNINSTALL_INCLUDE
  !error "UNINSTALL_INCLUDE is required."
!endif
!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE is required."
!endif

!define PRODUCT_NAME "Cafe Launcher"
!define PUBLISHER "BlueArchive Cafe"
!define EXECUTABLE_NAME "Cafe.Launcher.Avalonia.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Cafe.Launcher.Avalonia"

Name "Cafe Launcher"
Caption "Cafe Launcher Setup"
BrandingText "BlueArchive Cafe"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\Cafe Launcher"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "${FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "${PUBLISHER}"

Var DeleteApplicationDataCheckbox
Var DeleteApplicationData

!define MUI_ABORTWARNING
!define MUI_ICON "..\Assets\app-icon.ico"
!define MUI_UNICON "..\Assets\app-icon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXECUTABLE_NAME}"
!define MUI_LANGDLL_REGISTRY_ROOT "HKLM"
!define MUI_LANGDLL_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.ApplicationDataPageCreate un.ApplicationDataPageLeave
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Japanese"

LangString RunningApplicationMessage ${LANG_ENGLISH} "Cafe Launcher is running. Close it, then select Retry."
LangString RunningApplicationMessage ${LANG_SIMPCHINESE} "Cafe Launcher 正在运行。请将其关闭，然后选择“重试”。"
LangString RunningApplicationMessage ${LANG_JAPANESE} "Cafe Launcher が実行中です。終了してから「再試行」を選択してください。"
LangString DeleteDataTitle ${LANG_ENGLISH} "Application data"
LangString DeleteDataTitle ${LANG_SIMPCHINESE} "应用程序数据"
LangString DeleteDataTitle ${LANG_JAPANESE} "アプリケーションデータ"
LangString DeleteDataSubtitle ${LANG_ENGLISH} "Choose whether to remove data for the user running this uninstaller."
LangString DeleteDataSubtitle ${LANG_SIMPCHINESE} "选择是否删除执行此卸载程序的用户数据。"
LangString DeleteDataSubtitle ${LANG_JAPANESE} "このアンインストーラーを実行しているユーザーのデータを削除するか選択してください。"
LangString DeleteDataCheckboxText ${LANG_ENGLISH} "Delete $LOCALAPPDATA\Cafe Launcher"
LangString DeleteDataCheckboxText ${LANG_SIMPCHINESE} "删除 $LOCALAPPDATA\Cafe Launcher"
LangString DeleteDataCheckboxText ${LANG_JAPANESE} "$LOCALAPPDATA\Cafe Launcher を削除する"
LangString InvalidInstallLocation ${LANG_ENGLISH} "The registered installation directory is invalid. Uninstall was stopped."
LangString InvalidInstallLocation ${LANG_SIMPCHINESE} "注册的安装目录无效。卸载已停止。"
LangString InvalidInstallLocation ${LANG_JAPANESE} "登録されたインストール先が無効です。アンインストールを中止しました。"
LangString PreviousUninstallFailed ${LANG_ENGLISH} "The previous version could not be removed. Setup was stopped."
LangString PreviousUninstallFailed ${LANG_SIMPCHINESE} "无法删除旧版本。安装已停止。"
LangString PreviousUninstallFailed ${LANG_JAPANESE} "以前のバージョンを削除できませんでした。セットアップを中止しました。"

Function IsApplicationRunning
  nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /FI "IMAGENAME eq ${EXECUTABLE_NAME}" /FO CSV /NH'
  Pop $0
  Pop $1
  ${StrStr} $2 $1 "${EXECUTABLE_NAME}"
  StrCmp $2 "" notRunning
  Push "1"
  Return
notRunning:
  Push "0"
FunctionEnd

Function EnsureApplicationStopped
checkAgain:
  Call IsApplicationRunning
  Pop $0
  StrCmp $0 "0" done
  IfSilent silentBlocked
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(RunningApplicationMessage)" IDRETRY checkAgain
  Abort
silentBlocked:
  SetErrorLevel 1
  Quit
done:
FunctionEnd

Function UninstallExisting
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "UninstallString"
  StrCmp $0 "" done

  Push $1
  Push $2
  Push $3
  StrCpy $3 ""
  StrCpy $2 $0 1
  StrCmp $2 '"' quotedLoop unquotedLoop

unquotedLoop:
  StrCpy $2 $0 1 $3
  IntOp $3 $3 + 1
  StrCmp $2 "" parsed
  StrCmp $2 " " parsed unquotedLoop

quotedLoop:
  StrCmp $3 "" 0 +2
  StrCpy $0 $0 "" 1
  IntOp $3 $3 + 1
  StrCpy $2 $0 1 $3
  StrCmp $2 "" parsed
  StrCmp $2 '"' 0 quotedLoop

parsed:
  StrCpy $2 $0 $3
  GetFullPathName $3 "$2\.."
  IfFileExists "$2" 0 staleRegistration
  ExecWait '"$2" /S _?=$3' $1
  IntCmp $1 0 cleanup failed failed

cleanup:
  Delete "$2"
  RMDir "$3"
  Pop $3
  Pop $2
  Pop $1
  Goto done

staleRegistration:
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  Pop $3
  Pop $2
  Pop $1
  Goto done

failed:
  Pop $3
  Pop $2
  Pop $1
  MessageBox MB_OK|MB_ICONSTOP "$(PreviousUninstallFailed)"
  Abort

done:
FunctionEnd

Function .onInit
  SetRegView 64
  SetShellVarContext all
  !insertmacro MUI_LANGDLL_DISPLAY

  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $0 "" noExistingLocation
  StrCpy $INSTDIR "$0"

noExistingLocation:
  Call EnsureApplicationStopped
  Call UninstallExisting
FunctionEnd

Section "Cafe Launcher" SEC_APPLICATION
  SectionIn RO
  SetRegView 64
  SetShellVarContext all
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_GLOB}"

  FileOpen $0 "$INSTDIR\.cafe-launcher-install" w
  FileWrite $0 "Cafe.Launcher.Avalonia$\r$\n"
  FileClose $0

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Cafe Launcher"
  CreateShortcut "$SMPROGRAMS\Cafe Launcher\Cafe Launcher.lnk" "$INSTDIR\${EXECUTABLE_NAME}"
  CreateShortcut "$SMPROGRAMS\Cafe Launcher\Uninstall Cafe Launcher.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${EXECUTABLE_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
  SetShellVarContext all
  CreateShortcut "$DESKTOP\Cafe Launcher.lnk" "$INSTDIR\${EXECUTABLE_NAME}"
SectionEnd

Function un.IsApplicationRunning
  nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /FI "IMAGENAME eq ${EXECUTABLE_NAME}" /FO CSV /NH'
  Pop $0
  Pop $1
  ${UnStrStr} $2 $1 "${EXECUTABLE_NAME}"
  StrCmp $2 "" notRunning
  Push "1"
  Return
notRunning:
  Push "0"
FunctionEnd

Function un.EnsureApplicationStopped
checkAgain:
  Call un.IsApplicationRunning
  Pop $0
  StrCmp $0 "0" done
  IfSilent silentBlocked
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(RunningApplicationMessage)" IDRETRY checkAgain
  Abort
silentBlocked:
  SetErrorLevel 1
  Quit
done:
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  !insertmacro MUI_UNGETLANGUAGE

  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $0 "" invalidLocation
  StrCmp $0 "$INSTDIR" 0 invalidLocation
  IfFileExists "$INSTDIR\.cafe-launcher-install" 0 invalidLocation
  IfFileExists "$INSTDIR\${EXECUTABLE_NAME}" 0 invalidLocation
  Call un.EnsureApplicationStopped
  Return

invalidLocation:
  MessageBox MB_OK|MB_ICONSTOP "$(InvalidInstallLocation)"
  Abort
FunctionEnd

Function un.ApplicationDataPageCreate
  StrCpy $DeleteApplicationData "0"
  IfSilent skipApplicationDataPage

  !insertmacro MUI_HEADER_TEXT "$(DeleteDataTitle)" "$(DeleteDataSubtitle)"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateCheckbox} 0 20u 100% 14u "$(DeleteDataCheckboxText)"
  Pop $DeleteApplicationDataCheckbox
  ${NSD_SetState} $DeleteApplicationDataCheckbox ${BST_UNCHECKED}
  nsDialogs::Show

skipApplicationDataPage:
FunctionEnd

Function un.ApplicationDataPageLeave
  ${NSD_GetState} $DeleteApplicationDataCheckbox $DeleteApplicationData
FunctionEnd

Section "Uninstall"
  SetRegView 64
  SetShellVarContext all

  Delete "$DESKTOP\Cafe Launcher.lnk"
  Delete "$SMPROGRAMS\Cafe Launcher\Cafe Launcher.lnk"
  Delete "$SMPROGRAMS\Cafe Launcher\Uninstall Cafe Launcher.lnk"
  RMDir "$SMPROGRAMS\Cafe Launcher"

  !include "${UNINSTALL_INCLUDE}"

  DeleteRegKey HKLM "${UNINSTALL_KEY}"

  StrCmp $DeleteApplicationData "1" 0 preserveApplicationData
  RMDir /r "$LOCALAPPDATA\Cafe Launcher"

preserveApplicationData:
SectionEnd
