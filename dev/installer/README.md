# 꿈플 설치 도우미 (전체판 꿈플-설치.exe)

## 자기 업데이트 (2.2.5부터)

설치 도우미를 켜면 설치 화면보다 먼저 서명된 `update.json`(`https://raw.githubusercontent.com/junhyeonknight/kkumple/main/update.json`)을 확인합니다.

1. 전자서명, 제품, 유효기간을 확인한 뒤 `installer` 항목(version, url, sha256, bytes)을 읽습니다.
2. 이 버전보다 새로우면 `%LOCALAPPDATA%\Kkumple\Installer\<버전>\`에 받고, 크기와 SHA-256을 확인합니다.
3. 실행 중인 exe의 이름을 `.old-xxxx`로 바꾸고 새 파일을 원래 이름으로 넣은 다음, `--updated-from <옛 버전>`으로 새 버전을 실행하고 끝냅니다. 원래 위치에 쓸 수 없으면 받은 위치에서 실행합니다.
4. 새 버전은 시작할 때 `.old-*` 파일과 지난 버전 캐시를 지웁니다.

인터넷 오류, 서명 오류, 해시 불일치가 있거나 '건너뛰기'를 누르면 지금 버전으로 그대로 진행합니다. 오류는 `%LOCALAPPDATA%\Kkumple\setup.log`에 남깁니다.

번들 안의 경량판 `Kkumple.Setup.exe`(본체 미포함, CAD '꿈플 관리' 버튼)는 자기 업데이트를 하지 않습니다. 이 파일은 CAD 자동 업데이트 패키지로 교체됩니다.

## CAD를 켤 때 설치 도우미 갱신 (2.2.6 / 꿈플 2.0.8부터)

- 전체판은 실행될 때마다 자기 위치를 `%LOCALAPPDATA%\Kkumple\installer-locations.txt`에 기록합니다. 최대 10개이며, 임시 폴더와 자체 캐시는 제외합니다.
- CAD를 켜면 `Kkumple.mnl`이 `Kkumple.Setup.exe --refresh-installer`를 화면 없이 한 번 실행합니다. CAD는 기다리지 않습니다.
- 이 모드는 기록된 파일 중 어셈블리 이름이 `꿈플-설치`인 것만 대상으로 합니다. 서명된 `installer` 버전보다 낮으면 받아서 해시를 확인한 뒤 `File.Replace`로 교체합니다.
- 설치 도우미가 열려 있으면 교체를 미루고 다음 CAD 시작 때 다시 시도합니다. 기록된 파일이 없으면 인터넷도 쓰지 않습니다.
- 결과는 `%LOCALAPPDATA%\Kkumple\installer-refresh.log`에 남습니다.
- 위치가 기록되지 않은 2.2.4 이하 설치기는 CAD 시작 갱신 대상이 아닙니다. 그런 파일은 사용자가 2.2.6을 한 번 받아 실행하면 그때부터 대상이 됩니다.

옵션:
- `--no-self-update`: 확인을 건너뜁니다.
- `--self-update-as-version 2.2.0`: 시험용입니다. 현재 버전을 낮춘 것처럼 동작해 업데이트 경로를 확인합니다.

## 새 설치 도우미 배포 순서

1. `src\Setup\Program.cs`의 `AssemblyVersion`/`AssemblyFileVersion`을 올리고 `.\build.ps1`을 실행합니다. 전체판과 경량판이 함께 만들어집니다.
   경량판 `build\Kkumple.Setup.exe`는 다음 꿈플 업데이트 패키지의 모든 계열(`Contents\Versions\<계열>\`)에 넣습니다.
2. GitHub 릴리스 `installer-v<버전>`에 다음 두 파일을 올립니다. Latest 표시는 하지 않습니다.
   - `kkumple-setup-<버전>.exe`: 자기 업데이트용 exe
   - `kkumple-install-<버전>.zip`: 사람이 받는 배포 묶음
3. 꿈플 패키지만 들어 있는 서명본 `update.json`에 설치기 정보를 넣어 다시 서명합니다.
   ```
   ..\auto-update\tools\add-installer-to-manifest.ps1 -SignedManifest <패키지만 있는 update.json> -PrivateKey <운영 개인키> `
     -InstallerPath <kkumple-setup-x.exe> -InstallerVersion x.y.z -InstallerUrl <릴리스 exe 주소> -OutputDirectory <새 빈 폴더>
   ```
4. 결과 `update.json`으로 `main`을 교체합니다. 반드시 릴리스 파일을 먼저 올린 뒤에 교체하세요.

꿈플 본체 업데이트(new-update-release.ps1)로 새 `update.json`을 만들 때도, 설치기 자동 업데이트를 유지하려면 3단계를 다시 거쳐야 합니다. 그렇지 않으면 `installer` 항목이 빠집니다.

## 시험

- `tests\SelfUpdateTests.cs`: 서명된 설치기 정보 읽기, 위조 서명·http 주소·해시/버전 형식·크기 초과·내용 변조를 거부하는지 시험합니다(8개).
- 2026-11-20 실제 확인: 공개된 2.2.5를 `--self-update-as-version 2.2.0`으로 실행했을 때, 받기 → 해시 확인 → 자기 교체 → 재실행(`--updated-from 2.2.0`) → 설치 화면 표시까지 성공했고 옛 파일도 지워졌습니다.
