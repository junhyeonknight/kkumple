# 꿈플 원격 서비스 종료 점검기 2.2.0

2026-11-17 기준. 기존 꿈플 바이너리를 수정하지 않는 별도 점검기입니다.

## 사용자 PC에 설치

1. 배포 ZIP을 **폴더 전체로 압축 해제**합니다.
2. `자동점검-설치.cmd` 또는 `꿈플-설치.exe`를 더블클릭합니다.
3. 고지문을 읽고 체크한 다음 **동의하고 설치**를 누릅니다.

현재 로그인한 사용자의 `%LOCALAPPDATA%\Kkumple\ServiceEndAgent`에 점검기를 복사하고, `KkumpleServiceEnd-<사용자 SID>`라는 예약 작업을 등록합니다. 기본 설정은 **매일 오전 9시**, 현재 사용자 권한, **실제 삭제 모드(`DryRun=false`)**입니다. 관리자 권한으로 실행할 필요가 없습니다. 설치할 사용자 계정에서 일반 실행하세요.

**이 패키지에는 꿈플 본체 설치파일이 없습니다.** 기존에 꿈플을 설치한 PC에 자동 점검기를 추가하는 패키지입니다. 이 작업에서는 운영용 점검기를 현재 PC에 설치하지 않았습니다.

Windows 작업 스케줄러는 로그인한 사용자 토큰으로 실행하며 암호를 저장하지 않습니다. PC가 꺼져 있거나 로그아웃 상태이면 실행되지 않습니다. 놓친 예약은 이후 실행 가능한 때 지연 실행되도록 설정했습니다. 즉시 실행이나 정확히 24시간 이내 종료를 보장하지 않습니다. [Microsoft: InteractiveToken](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/ne-taskschd-task_logon_type), [놓친 작업 실행](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-startwhenavailable).

## 종료 동작과 보호 범위

- 상태 URL: `https://raw.githubusercontent.com/junhyeonknight/kkumple/main/status.json`
- RSA-2048/SHA-256 서명과 서비스 이름을 확인한 `ended:true` 신호에만 반응합니다. 미래 적용일은 기다리고, 잘못된 적용일·서비스 이름·서명은 거부합니다.
- 네트워크 오류·응답 오류이면 삭제하지 않습니다. 배포 설정의 `ExpiryDateIso`는 비어 있어 날짜만으로 종료하지 않습니다.
- 정리 대상은 `%APPDATA%\Autodesk\ApplicationPlugins\KkumpleFree.bundle` 하나입니다. `.kkumple-owner`에 `KKUMPLE_FREE_NONCOMMERCIAL_V2`가 있어야 합니다.
- `.dwg`, `.dxf`, `.dwt`, `.dws`, `.bak`는 대소문자와 관계없이 보존합니다. 보호 파일이 있으면 그 파일과 폴더·소유자 표식을 남기며, 제거할 수 있는 프로그램 파일만 정리합니다. 그 밖의 개인 자료는 번들 밖에 보관하세요.
- 허용 범위 밖, 시스템·프로그램 폴더, 사용자 주요 폴더 자체, 드라이브 루트, 상대·네트워크 경로, 와일드카드, 연결 폴더·심볼릭 링크는 거부합니다.
- 실행 중인 AutoCAD가 DLL을 사용하면 삭제가 실패할 수 있습니다. **AutoCAD를 강제로 종료하거나 플러그인을 강제 언로드하지 않습니다.** 삭제 실패 시 소유자 표식과 예약 작업을 보존하고 다음 점검에서 재시도합니다.
- 정리가 성공하면 예약 작업을 해제합니다. 점검기 EXE와 설정·로그는 확인 및 수동 제거를 위해 남깁니다. 이전 `SelfRemove` 설정은 이 배포본에서 사용하지 않습니다.

## 점검기 제거와 기록

배포 폴더나 설치 폴더의 `자동점검-제거.cmd`를 실행합니다. 확인 후 현재 사용자의 점검기 파일과 전용 예약 작업을 제거합니다. 꿈플 본체 번들, 도면, 다른 파일, 동의 기록과 로그는 보존합니다. 재실행도 가능합니다.

- 동의 기록: `%LOCALAPPDATA%\Kkumple\InstallConsent\consent.json`
- 설치 기록: `%LOCALAPPDATA%\Kkumple\setup.log`
- 점검 기록: `%LOCALAPPDATA%\Kkumple\ServiceEndAgent\service-end.log`

상태 조회에 사용자 이름이나 PC 식별자를 추가하지 않습니다. 접속 서버에는 일반적인 인터넷 접속 정보(IP 등)가 보일 수 있습니다.

## 운영자: 신호 변경

기존 프로젝트의 `service-end/operator/status.json`은 정상 신호, `status-KILL.json`은 서명된 종료 신호입니다. 종료를 결정하면 GitHub의 `status.json`을 종료 신호 내용으로 교체합니다. 이 작업에서는 원격 파일을 변경하지 않았습니다. 검증 시 공개 파일은 서명이 유효한 **`ended:false`**였습니다.

`operator/privatekey.xml`은 기존 위치에 그대로 두었습니다. 읽거나 수정하거나 배포 패키지에 넣지 않았습니다. **배포 ZIP에는 개인키와 운영자 종료 신호가 없습니다.**

연습하려면 배포 폴더의 `config.json`에서 `DryRun=true`로 바꾼 뒤 설치합니다. 설치 후 배포 폴더 설정을 바꾸는 것만으로는 이미 복사된 설정이 바뀌지 않습니다. 다시 설치하여 반영하거나 설치 위치의 설정을 직접 수정하세요.

## 선택: 꿈플 본체 설치와 묶기

실제 꿈플 설치 EXE를 배포 폴더에 추가하고 `consent-config.json`의 `InstallerExe`에 상대 경로를 지정하면, **동의 → 실제 설치 EXE 실행 및 종료코드 0 확인 → 점검기 등록** 순서로 실행합니다. 실제 설치파일이 제공되지 않아 본체 설치까지의 통합 검증은 수행하지 않았습니다. 부트스트랩 설치파일이 별도 프로세스에 설치를 넘긴 뒤 먼저 종료하는 경우에는 별도 연동이 필요합니다. 묶어서 배포할 때에는 고지문도 해당 설치 내용에 맞게 변경하세요.

코드 서명 인증서는 제공되지 않아 EXE는 코드 서명하지 않았습니다.

## 소스와 재빌드

소스 프로젝트에는 `ServiceEndAgent/`, `InstallConsent/`, `배포/`, `tests/`가 있습니다. `build.ps1`은 Windows에 있는 .NET Framework C# 컴파일러로 두 EXE를 생성합니다. C# 5 호환으로 컴파일을 확인했습니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

검증 범위와 결과는 `검증결과.md`를 참조하세요. 대상 PC에 .NET Framework 4.8이 필요합니다.


## 2.2.0 자동 업데이트 연동

자동 업데이트 점검기와 같은 전역 잠금을 사용하여 번들을 동시에 변경하지 않게 했습니다. 기존 설치자는 이 배포본으로 다시 설치하세요. 변경 후 기존 86개 검증을 다시 통과했습니다. 자동 업데이트 연결과 새 배포 도구는 별도 auto-update 프로젝트를 참조하세요.
