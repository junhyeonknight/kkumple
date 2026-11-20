@echo off
chcp 65001 >nul
title 꿈플 업데이트 복구
echo.
echo  꿈플 업데이트 복구
echo  AutoCAD 2015에서 "Kkumple.MplForms.dll 사용 중"으로 업데이트가 실패할 때 한 번만 실행하세요.
echo.
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if not errorlevel 1 (
  echo  열려 있는 AutoCAD가 있습니다. AutoCAD를 모두 닫은 뒤 이 파일을 다시 실행해 주세요.
  echo.
  pause
  exit /b 1
)
set "UPDATER=%LOCALAPPDATA%\Kkumple\Updater\Kkumple.Updater.exe"
if not exist "%UPDATER%" (
  echo  이 PC에서 꿈플 자동 업데이트를 찾지 못했습니다. 꿈플 설치 도우미로 다시 설치해 주세요.
  echo.
  pause
  exit /b 1
)
echo  최신 꿈플을 확인하고 설치하고 있습니다. 잠시 기다려 주세요...
"%UPDATER%" --check
if errorlevel 1 (
  echo.
  echo  업데이트를 마치지 못했습니다. 화면의 안내를 확인한 뒤 다시 시도해 주세요.
  echo.
  pause
  exit /b 1
)
echo.
echo  완료했습니다. 이제 평소처럼 AutoCAD를 실행하세요.
echo.
pause
