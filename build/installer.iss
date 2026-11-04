; Inno Setup: одно-файловый установщик Dynamic Edge (внутри — подписанный MSIX и сертификат издателя).
#ifndef AppVersion
  #define AppVersion "1.0.0.0"
#endif
#ifndef ArtDir
  #define ArtDir "..\artifacts"
#endif

[Setup]
AppId={{B7E3C5A2-4D1F-4E8B-9A3C-5F2D6E8A1C47}
AppName=Dynamic Edge
AppVersion={#AppVersion}
AppPublisher=Dynamic Edge
CreateAppDir=no
Uninstallable=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#ArtDir}
OutputBaseFilename=DynamicEdge-Setup
SetupIconFile=..\src\DynamicEdge\Assets\app.ico
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
WelcomeLabel2=Программа установит Dynamic Edge — «динамический остров» для Windows 11.%n%nУстановщик добавит сертификат издателя в доверенные (он нужен Windows для установки пакета) и установит приложение. Удалить его можно позже: Параметры → Приложения.%n%nНажмите «Далее», чтобы продолжить.

[Files]
Source: "{#ArtDir}\DynamicEdge.msix"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#ArtDir}\DynamicEdge.cer"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -WindowStyle Hidden -Command ""Start-Process ('shell:AppsFolder\' + (Get-AppxPackage -Name DynamicEdge.App).PackageFamilyName + '!App')"""; \
  Description: "Запустить Dynamic Edge"; \
  Flags: postinstall nowait skipifsilent runasoriginaluser runhidden; Check: InstallOk

[Code]
var
  Installed: Boolean;

function InstallOk: Boolean;
begin
  Result := Installed;
end;

function ReadFileText(const FileName: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if LoadStringsFromFile(FileName, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
      Result := Result + Lines[I] + #13#10;
  if Length(Result) > 700 then
    Result := Copy(Result, 1, 700) + '...';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Params, ErrFile: String;
begin
  if CurStep = ssPostInstall then
  begin
    ErrFile := ExpandConstant('{tmp}\install_error.txt');
    Params :=
      '-NoProfile -ExecutionPolicy Bypass -Command "try { ' +
      'Import-Certificate -FilePath ''' + ExpandConstant('{tmp}\DynamicEdge.cer') +
      ''' -CertStoreLocation Cert:\LocalMachine\TrustedPeople -ErrorAction Stop | Out-Null; ' +
      'Add-AppxPackage -Path ''' + ExpandConstant('{tmp}\DynamicEdge.msix') +
      ''' -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop ' +
      '} catch { $_ | Out-String | Set-Content -Encoding Default -Path ''' + ErrFile + '''; exit 1 }"';
    WizardForm.StatusLabel.Caption := 'Установка Dynamic Edge...';
    Installed := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '',
                      SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
    if not Installed then
      MsgBox('Не удалось установить Dynamic Edge.' + #13#10#13#10 + ReadFileText(ErrFile), mbError, MB_OK);
  end;
end;
