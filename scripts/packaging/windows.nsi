# -*- coding: utf-8 -*-
; Machine-wide installation. Payload commands are generated from a verified manifest.
Unicode true
RequestExecutionLevel admin
SetCompressor /SOLID zlib
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"
!include "x64.nsh"
!include "Integration.nsh"
!include "${BUILD_METADATA}"

!define PRODUCT "Grid Mapper"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Viavision.GridMapper"
!define PROGID "Viavision.GridMapper"

Name "${PRODUCT}"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\Grid Mapper"
; MUI applies its icons when page macros expand; configure those defaults too.
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
BrandingText "VIAVISION · viavision.pro"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT}"
VIAddVersionKey /LANG=1033 "FileDescription" "Grid Mapper installer (Windows x64)"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "VIAVISION — viavision.pro"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install Grid Mapper for all users of this computer. Administrator permission is required.$\r$\n$\r$\nThe application includes the components needed for offline editing and PNG export. NDI Runtime is installed separately.$\r$\n$\r$\nSave your projects and images in your Documents folder."
!define MUI_FINISHPAGE_TEXT "Grid Mapper has been installed. Open it from the Start menu.$\r$\n$\r$\nSave projects and PNG images in your Documents folder. Read README.md or README.ru.md for instructions."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "Only installed Grid Mapper files will be removed. Your projects, PNG images and other added files will be kept.$\r$\n$\r$\nClose Grid Mapper before continuing."
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var DirectoryEmpty
Var RequestedDirectory

; NSIS replaces an invalid /D= path with InstallDir before .onInit. Read the
; original OS command line so a file destination cannot silently install elsewhere.
Function RestoreRequestedDirectory
  StrCpy $RequestedDirectory 0
  System::Call 'kernel32::GetCommandLineW() w .r0'
  StrCpy $1 0
  scan:
    StrCpy $2 $0 4 $1
    StrCmp $2 "" done
    StrCmp $2 " /D=" found
    IntOp $1 $1 + 1
    Goto scan
  found:
    IntOp $1 $1 + 4
    StrCpy $INSTDIR $0 "" $1
    StrCpy $RequestedDirectory 1
  done:
FunctionEnd

Function .onInit
  SetShellVarContext all
  SetRegView 64
  ${IfNot} ${AtLeastWin10}
  ${OrIfNot} ${IsNativeAMD64}
    MessageBox MB_OK|MB_ICONSTOP "Grid Mapper requires Windows 10/11 x64." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  Call RestoreRequestedDirectory
  ${If} $RequestedDirectory == 1
    Call DirectoryLeave
  ${EndIf}
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $0 != ""
    MessageBox MB_OK|MB_ICONSTOP "Grid Mapper is already installed. Uninstall it using Installed apps before installing this version." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
FunctionEnd

; An empty destination avoids overwriting unrelated files or an open older version.
Function CheckDirectory
  StrCpy $DirectoryEmpty 1
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i .r2'
  IntCmp $2 -1 done
  IntOp $2 $2 & 16
  IntCmp $2 0 not_empty
  FindFirst $0 $1 "$INSTDIR\*"
  loop:
    StrCmp $1 "" finish
    StrCmp $1 "." next
    StrCmp $1 ".." next
    StrCpy $DirectoryEmpty 0
    Goto finish
  next:
    FindNext $0 $1
    Goto loop
  finish:
    FindClose $0
    Goto done
  not_empty:
    StrCpy $DirectoryEmpty 0
  done:
FunctionEnd

Function DirectoryLeave
  Call CheckDirectory
  ${If} $DirectoryEmpty != 1
    MessageBox MB_OK|MB_ICONSTOP "Choose a new or empty folder. Existing files will not be overwritten." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
FunctionEnd

Section "Application and Start menu shortcut" MainSection
  SectionIn RO
  Call DirectoryLeave
  SetErrorLevel 0
  ClearErrors
  SetOutPath "$INSTDIR"
  CreateDirectory "$INSTDIR\Metadata"
  IfErrors installation_failed
  WriteINIStr "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Product" "Viavision.GridMapper"
  WriteINIStr "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Version" "${VERSION}"
  WriteINIStr "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Path" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors installation_failed
  !include "${PAYLOAD_INSTALL}"
  SetOutPath "$INSTDIR"
  File /oname=NSIS-COPYING.txt "${NSIS_COPYING}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "VIAVISION — viavision.pro"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\GridMapper.Desktop.exe,0"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" ${SIZE_KIB}
  SetOutPath "$DOCUMENTS"
  CreateDirectory "$SMPROGRAMS\${PRODUCT}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\Grid Mapper.lnk" "$INSTDIR\GridMapper.Desktop.exe"
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\Uninstall Grid Mapper.lnk" "$INSTDIR\Uninstall.exe"
  IfErrors installation_failed
  Goto installation_done
  installation_failed:
    MessageBox MB_OK|MB_ICONSTOP "Installation could not be completed. If Uninstall.exe was created, use it to remove the files from this attempt, then choose a new empty folder." /SD IDOK
    SetErrorLevel 1
    Abort
  installation_done:
SectionEnd

Section /o "Desktop shortcut" DesktopSection
  CreateShortcut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\GridMapper.Desktop.exe"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "The application was installed, but the desktop shortcut could not be created." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd

Section "Add .gpm projects to Open with" AssociationSection
  WriteRegStr HKLM "Software\Classes\${PROGID}" "" "Grid Mapper project"
  WriteRegStr HKLM "Software\Classes\${PROGID}\Application" "ApplicationName" "${PRODUCT}"
  WriteRegStr HKLM "Software\Classes\${PROGID}\DefaultIcon" "" "$INSTDIR\GridMapper.Desktop.exe,0"
  WriteRegStr HKLM "Software\Classes\${PROGID}\shell\open\command" "" '$\"$INSTDIR\GridMapper.Desktop.exe$\" $\"%1$\"'
  WriteRegStr HKLM "Software\Classes\.gpm\OpenWithProgids" "${PROGID}" ""
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "The application was installed, but Open with registration could not be completed." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  ${NotifyShell_AssocChanged}
SectionEnd

Function un.onInit
  SetShellVarContext all
  SetRegView 64
  ReadINIStr $0 "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Product"
  ReadINIStr $1 "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Version"
  ReadINIStr $2 "$INSTDIR\Metadata\installation-receipt.ini" "Installation" "Path"
  ${If} $0 != "Viavision.GridMapper"
  ${OrIf} $1 != "${VERSION}"
  ${OrIf} $2 != "$INSTDIR"
    MessageBox MB_OK|MB_ICONSTOP "No matching installation record was found in this folder. Uninstall was cancelled." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
FunctionEnd

!macro DeleteInstalledFile PATH
  ClearErrors
  Delete "${PATH}"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Could not delete file:$\r$\n${PATH}$\r$\n$\r$\nClose Grid Mapper and try uninstalling again." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
!macroend

Section "Uninstall"
  ; Delete the executable first: a running instance must not lose its DLLs.
  !insertmacro DeleteInstalledFile "$INSTDIR\GridMapper.Desktop.exe"
  !include "${PAYLOAD_UNINSTALL}"
  !insertmacro DeleteInstalledFile "$INSTDIR\NSIS-COPYING.txt"
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $0 == "$INSTDIR"
    DeleteRegKey HKLM "${UNINSTALL_KEY}"
    DeleteRegValue HKLM "Software\Classes\.gpm\OpenWithProgids" "${PROGID}"
    DeleteRegKey /ifempty HKLM "Software\Classes\.gpm\OpenWithProgids"
    DeleteRegKey /ifempty HKLM "Software\Classes\.gpm"
    DeleteRegKey HKLM "Software\Classes\${PROGID}"
    Delete "$SMPROGRAMS\${PRODUCT}\Grid Mapper.lnk"
    Delete "$SMPROGRAMS\${PRODUCT}\Uninstall Grid Mapper.lnk"
    RMDir "$SMPROGRAMS\${PRODUCT}"
    Delete "$DESKTOP\${PRODUCT}.lnk"
    ${NotifyShell_AssocChanged}
  ${EndIf}
  !insertmacro DeleteInstalledFile "$INSTDIR\Metadata\installation-receipt.ini"
  RMDir "$INSTDIR\Metadata"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
