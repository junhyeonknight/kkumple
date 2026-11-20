# 꿈플 개발 기록 (Codex 이어받기용)

이 문서는 2026-11-20까지의 꿈플(Kkumple) 개발 진행 상황과 경위를 정리한 것입니다. 다른 에이전트(Codex 등)가 이어서 작업할 수 있도록 썼습니다.
`dev/` 아래 소스는 원래 작업 폴더 `E:\꿈플-원격스위치`를 그대로 옮긴 것입니다(빌드 결과물·원본 모듈·운영 키 제외).

> **절대 올리지 말 것:** 운영 개인키 `service-end\operator\privatekey.xml`. 서명은 원래 PC에서 로컬로만 합니다. 이 저장소는 공개입니다.

## 1. 꿈플이 무엇인가

- DreamPlus에서 파생된 AutoCAD 플러그인. 본체 `꿈플.exe`(2.0.3)와 모듈 DLL의 **소스는 없음**. 우리가 만든 것은 설치 도우미, 자동 업데이트, 원격 종료 점검기, 메뉴 LISP(mnl) 수정입니다.
- 설치 위치: `%APPDATA%\Autodesk\ApplicationPlugins\KkumpleFree.bundle` (`PackageContents.xml`, `Contents\Versions\<계열>`)
- 모듈 계열: `2010`(2010–2012), `2013`(2013–2014), `2015`(2015–2024), `2025`(2025–2027). R18.0–R26.0만 지원.

## 2. 현재 공개 버전 (2026-11-20)

| 구분 | 버전 | 위치 |
|---|---|---|
| 꿈플 패키지(자동 업데이트) | **2.0.11** | 릴리스 `bundle-v2.0.11` (Latest) |
| 설치 도우미 | **2.2.7** | 릴리스 `installer-v2.2.7` |
| 업데이트 안내 | `update.json` (main 루트, 커밋 36a2721) | `https://raw.githubusercontent.com/junhyeonknight/kkumple/main/update.json` |

`status.json`(원격 스위치 신호)은 건드리지 않습니다.

## 3. 폴더 구성

| 폴더 | 내용 |
|---|---|
| `auto-update/Updater` | 업데이터 `Kkumple.Updater.exe` 소스 (UpdateCore: 서명 검증·다운로드·ZIP 검사·폴더 교체·복구, GateInstaller: CAD 시작 점검 LISP 연결) |
| `auto-update/tools` | `new-update-release.ps1`(패키지 ZIP + 서명), `add-installer-to-manifest.ps1`(설치기 항목 추가 재서명) |
| `auto-update/releases/2.0.7~2.0.11` | 각 버전의 mnl 소스, PackageContents, 서명된 update.json, 릴리스 노트 |
| `installer` | 설치 도우미 `꿈플-설치.exe`(전체판) / `Kkumple.Setup.exe`(경량판) 소스, `build.ps1`, 테스트 |
| `service-end` | 원격 종료 점검기(ServiceEndAgent), 설치 동의(InstallConsent), 서명 도구. `operator/`는 공개키만 포함 |
| `legacy-2008-2009` | AutoCAD 2008/2009 시험 연결기 0.2 (**보류**, 7장 참고) |
| `tools/kk2008-receiver` | 2008 PC 진단 보고서 수신기 (중계 주소는 공개 저장소라 가림, 7장 참고) |
| `tools/ego-publish` | GitHub 릴리스·update.json 게시에 쓴 ego-browser 스크립트. `kkumple-2.0.8-publish/release-lib.mjs`가 재사용 라이브러리 |
| `남은작업-코덱스.md` | 2026-11-17 자동 업데이트 첫 인수인계 문서 |

## 4. 동작 구조

1. **CAD 시작 점검:** 업데이트 게이트 LISP가 `%LOCALAPPDATA%\Kkumple\Updater\Kkumple.Updater.exe --check`를 동기 실행. `RequireOnlineCheck` 켜짐 → 필수 업데이트 실패 시 그 실행에서는 꿈플을 불러오지 않음.
2. **업데이터:** `update.json`(RSA-SHA256 서명 봉투 `{payload, signature}`)을 받아 검증. 캐시된 `last-verified-update.json`보다 낮은 버전은 거부(롤백 방지). 계열 ZIP을 받아 `Versions\<계열>` 폴더를 **통째로 교체**. 교체 전 `PreflightLocks`가 폴더 안 파일 잠금을 검사해 하나라도 잠겨 있으면 실패. 임시 폴더 `.kkupd-stage-*`, 백업 `.kkupd-backup-*`.
3. **mnl(`Kkumple.mnl`):** 메뉴와 함께 모든 문서에서 실행. 클라이언트 쪽 수정은 패키지 업데이트로 mnl에 넣어 배포.
   - 2.0.7: 사용자 Support 폴더가 지원 경로에서 빠져 `acad.pgp`를 못 읽던 문제 복구 (`vla-put-SupportPath` + `(setvar "RE-INIT" 16)`).
   - 2.0.8: CAD 시작 시 `Kkumple.Setup.exe --refresh-installer` 실행(설치 도우미 파일 백그라운드 갱신).
   - 2.0.9: **Versions 안의 파일은 직접 실행/NETLOAD 금지.** `kkf:shadow-copy`로 `%LOCALAPPDATA%\Kkumple\Shadow\<종류>\<크기-시간>\`에 복사 후 사용. (`Kkumple.Updater.Next.exe --activate-bundled`만 예외: 코드가 그 경로를 요구하고 같은 뮤텍스 사용)
   - 2.0.11: **NETLOAD할 DLL 복사본은 `Contents\Loaders\Kkumple.MplForms.<크기-시간>.dll`.** Loaders는 이미 TRUSTEDPATHS에 있어 SECURELOAD 경고가 안 뜨고, 업데이터가 교체하지 않는 폴더라 잠겨도 무방. 이전 복사본은 사용 중이 아닐 때 삭제. (Shadow 폴더는 실행 파일 `Kkumple.Setup.exe` 복사본에만 계속 사용 — exe 실행은 SECURELOAD 대상 아님)
4. **설치 도우미 자기 업데이트 (2.2.5~):** `update.json`의 `installer` 항목 `{version,url,sha256,bytes}`를 읽어 `%LOCALAPPDATA%\Kkumple\Installer\<ver>`에 받고, 실행 중 exe를 `.old-*`로 바꾼 뒤 새 파일로 교체·재실행(`--updated-from`). 실행 위치는 `%LOCALAPPDATA%\Kkumple\installer-locations.txt`에 기록(최대 10개).
5. **CAD 시작 시 갱신 (2.2.6~):** `--refresh-installer`가 기록된 설치 도우미 파일(어셈블리 이름 "꿈플-설치"만)을 `File.Replace`로 교체. 로그 `%LOCALAPPDATA%\Kkumple\installer-refresh.log`.
6. **정리 (2.2.7~):** `active-update.json`이 없을 때 30분 넘은 `.kkupd-stage-<32hex>` 폴더, 14일 넘은 Shadow 폴더 삭제.

## 5. 버전 경위 (스토리)

- **2.0.3 / 설치기 2.2.0~2.2.4 (11-17~18):** 자동 업데이트·원격 스위치 도입. 최신 원본 배포본은 `꿈플-배포용-2.2.4.zip`(원래 PC의 `D:\codex\2026-11-18\new-chat-6\outputs`).
- **2.0.5:** AutoCAD 2015용 MplForms 수정 모듈 추가. mnl이 `Versions\2015\Kkumple.MplForms.dll`을 직접 NETLOAD → **같은 CAD가 파일을 잠가 업데이트가 영구히 막히는 결함**의 원인이 됨.
- **2.0.7 (11-20):** "L, CO, TR 같은 기본 명령/별칭이 2~3초 기다려야 인식됨" 신고. 원인은 acad.pgp가 있는 사용자 Support 폴더가 지원 경로에서 빠진 것. mnl로 경로 복구.
- **설치기 2.2.5:** 설치 도우미를 켜자마자 업데이트 확인 후 자기 교체.
- **2.0.8 / 설치기 2.2.6:** 사용자는 보통 CAD만 켜므로 CAD 시작 때 설치 도우미도 갱신. 테스트 번들 4개 정리.
- **2.0.9 / 설치기 2.2.7:** AutoCAD 2015에서 "꿈플 파일을 사용 중인 다른 AutoCAD를 종료한 뒤 다시 시도해 주세요: Kkumple.MplForms.dll"로 업데이트 실패 → 꿈플 미로드. shadow-copy로 근본 수정. 이미 2.0.5에 막힌 PC는 클라이언트가 원격으로 못 고치므로 **CAD를 닫고 `kkumple-update-repair.cmd`(bundle-v2.0.9 자산) 1회 실행** 필요. 개발 PC는 이 방법으로 2.0.9 복구 완료, CAD에서 정상 동작 확인.
- **2.0.10 (커밋 b373f50):** 2.0.9 이후 AutoCAD 2015를 켤 때마다 "파일 로드 - 보안 경고"(Shadow\MplForms\...\Kkumple.MplForms.dll) 발생. 원인은 SECURELOAD — Shadow 폴더가 TRUSTEDPATHS 밖. mnl에서 `setvar TRUSTEDPATHS`로 등록하도록 함. 동작은 했지만 AutoCAD가 TRUSTEDPATHS를 종료 시 저장해서 첫 실행에 경고가 또 뜸.
- **2.0.11 (커밋 36a2721):** 복사본을 이미 신뢰된 `Contents\Loaders`에 두는 방식으로 변경, 2.0.10이 넣은 Shadow 신뢰 항목은 제거. 개발 PC에서 **경고 사라짐 확인**.
- 교훈: AutoCAD가 켜진 상태에서 `Kkumple.Updater.exe --check`를 직접 돌리면 Versions의 기본 모듈(KkCivil0.dll 등)이 잠겨 있어 반드시 실패하고 사용자에게 실패 창이 뜸. 적용은 CAD 재시작 시 게이트에 맡길 것. `--check` 성공 시 "꿈플 업데이트 완료" 패치노트 창이 떠서 닫을 때까지 프로세스가 안 끝남. raw.githubusercontent main/update.json은 최대 ~5분 캐시.

## 6. 배포 절차 (새 꿈플 버전)

1. `auto-update/releases/<새버전>/source`에 mnl·PackageContents 준비 → 계열별 ZIP.
2. `new-update-release.ps1`로 패키지 ZIP + update.json 서명 (개인키는 로컬 경로에서만 읽음).
3. `add-installer-to-manifest.ps1`로 `installer` 항목 추가·재서명. **이 단계를 빼면 설치 도우미 자기 업데이트/CAD 시작 갱신이 멈춤.** (이미 항목이 있으면 거부)
4. GitHub 릴리스(`bundle-vX.Y.Z`)에 ZIP 업로드 → main의 `update.json`을 서명본으로 교체. 게시 전 main의 현재 update.json이 직전에 올린 것과 같은지 확인(`round-02.mjs` 참고).
5. 설치 도우미 새 버전은 `installer/build.ps1` → `installer-vX.Y.Z` 릴리스에 `kkumple-install-X.zip`, `kkumple-setup-X.exe`.
- 자산 이름은 **ASCII만** (한글 이름은 GitHub에서 깨짐).
- 원래 PC에는 git/gh가 없어서 게시는 ego-browser(GitHub 웹 UI)로 했음.
- 검증: update.json 서명은 JavaScriptSerializer로 확인 (PowerShell ConvertFrom-Json은 오탐).

## 7. AutoCAD 2008/2009 (보류)

- 요청: 2008~2014 호환 → 2010~2014는 이미 지원이라 2008/2009로 좁힘.
- 시험 연결기(`legacy-2008-2009`): 2010 계열 모듈을 그대로 쓰고, CUI 변환(2009 리본 2탭/17패널), ICO→BITMAP 아이콘 1,288개, 시작하기 세트 등록. 가짜 레지스트리 테스트만 통과.
- 위험: 2010 모듈에 R18 전용 API(PdfReference, Transparency, OpenCloseTransaction 등) 사용, 원본 소스 없음.
- 실제 2008 PC(Win10, 32비트 CAD)에서 `KKLOAD`뿐 아니라 LISP 한 줄 `(progn ...)`도 "알 수 없는 명령" → LT이거나 LISP 미지원으로 보임. 사용자가 한 번 포기했다가, 로그 자동 수집을 붙여 재개 요청 → **연결기 0.2** 제작 후 다시 **보류**. 요청이 없으면 재개하지 말 것.
- **연결기 0.2 (`legacy-2008-2009/src/Diag.cs`):** 연결기 실행 시·설치/제거 직후·오류·크래시·CAD 로더 실행 시 진단 보고서(PC/OS/.NET, AutoCAD·AutoCAD LT 제품과 acad.exe 버전, 시작하기 세트 값, 설치 폴더, 오류 스택, `kkl-log.txt`, CAD 명령행 기록 `LOGFILEMODE`→`Legacy17\cadlog`)를 3.4KB 조각으로 ntfy.sh 중계 주소에 POST. 도면 내용·창 제목은 보내지 않음. "CAD 실행 후 진단" 버튼(acad.exe 실행 90초 뒤 전송).
- 로더 LISP는 게이트 밖에서도 `kkl-log.txt`를 남기고 연결기 사본 `kkumple-legacy17-report.exe --send-report <이유> --delay N`을 `startapp`.
- **수신기 (`tools/kk2008-receiver/receiver.ps1`):** 중계 주소를 20초마다 폴링해 조각을 합쳐 개발 PC `D:\꿈플-2008-로그\*.txt`로 저장. `-Once`로 한 번만 받기. 중계 보관 약 12시간. 개발 PC에서 자기 시험 1건 수신 성공, **실제 2008 PC에서는 미실행**. 수신기는 현재 꺼져 있음(`수신기-켜기.cmd`).
- 중계 주소(topic)는 공개 저장소라 `kk2008-REDACTED-see-local-file`로 가림. 실제 값은 개발 PC의 `E:\꿈플-원격스위치\legacy-2008-2009\src\Diag.cs`와 `D:\꿈플-2008-로그\receiver.ps1`에 있음(둘이 같아야 함).
- GitHub으로 직접 받는 방식은 exe에 쓰기 토큰을 넣어야 해서(공개 저장소 변조 위험) 사용자가 배제함.

## 8. 남은 일 / 주의

- 다른 AutoCAD 2015 사용자 중 2.0.5에 막힌 PC: CAD 닫고 repair.cmd 1회 실행 안내 필요.
- 개발 PC `Versions` 안에 실패한 업데이트의 `.kkupd-stage-*` 4개, `.kkupd-backup-*` 3개가 남음. stage는 2.2.7이 CAD 시작 때 정리, backup 정리 정책은 아직 없음.
- 2.0.9~2.0.11 동작과 L/CO/TR 즉시 인식은 개발 PC(AutoCAD 2015)에서만 실사용 확인. 2010/2013/2025 계열 실기 검증 안 됨.
- 2.0.11의 Loaders 방식은 Loaders가 TRUSTEDPATHS에 있다는 전제(본체 설치기가 Versions\2015와 함께 등록). 다른 PC에서 경고가 다시 보고되면 그 PC의 TRUSTEDPATHS부터 확인.
- 2.0.10을 받은 PC는 TRUSTEDPATHS에 `...\Kkumple\Shadow\MplForms\<키>` 항목이 남았다가 2.0.11 첫 실행 후 CAD 종료 시 제거됨.
- 테스트: `installer/tests/SelfUpdateTests.cs`(8개 통과), `auto-update/tests`, `service-end/tests`.
- 빌드: .NET Framework 4 `csc.exe` 직접 호출(`build.ps1`). 한글 경로가 있으므로 ps1은 **UTF-8 BOM**으로 저장.
- 사용자 안내 문구·릴리스 노트는 모두 한국어.
