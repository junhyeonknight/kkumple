const { publishRelease } = await import("file:///C:/Users/은준현/ego/kkumple-2.0.8-publish/release-lib.mjs");
const task = await taskSpace("kkumple 2.0.9 and installer 2.2.7 publish");
const page = task.page("p1");
const inst = "E:/꿈플-원격스위치/installer/release/2.2.7/";
const r1 = await publishRelease(page, {
  tag: "installer-v2.2.7",
  title: "꿈플 설치 도우미 2.2.7",
  label: "none",
  body: [
    "CAD를 켤 때 실패한 업데이트가 남긴 임시 폴더를 자동으로 정리합니다.",
    "CAD를 켤 때 받아 둔 설치 도우미 파일도 백그라운드에서 최신으로 바뀌고, 설치 도우미를 직접 켜도 먼저 새 버전을 확인합니다.",
    "",
    "처음 설치할 때는 kkumple-install-2.2.7.zip을 받아 압축을 풀고 꿈플-설치.exe를 실행하세요. kkumple-setup-2.2.7.exe는 자동 업데이트에 쓰는 파일입니다.",
  ].join("\n"),
  files: [
    { path: inst + "kkumple-install-2.2.7.zip", asset: "kkumple-install-2.2.7.zip" },
    { path: inst + "kkumple-setup-2.2.7.exe", asset: "kkumple-setup-2.2.7.exe" },
  ],
});
console.log({ taskSpaceId: task.spaceId, ...r1 });
const b = "E:/꿈플-원격스위치/auto-update/releases/2.0.9/";
const r2 = await publishRelease(page, {
  tag: "bundle-v2.0.9",
  title: "꿈플 자동 업데이트 2.0.9",
  label: "latest",
  body: [
    "AutoCAD 2015에서 자동 업데이트가 \"꿈플 파일을 사용 중인 다른 AutoCAD를 종료한 뒤 다시 시도해 주세요: Kkumple.MplForms.dll\"로 실패하던 문제를 고쳤습니다. CAD가 업데이트 대상 폴더의 파일을 직접 불러오지 않고 복사본을 사용합니다.",
    "실패한 업데이트가 남긴 임시 폴더를 CAD 시작 시 자동으로 정리합니다.",
    "",
    "이미 이 오류가 난 AutoCAD 2015 PC는 AutoCAD를 모두 닫고 kkumple-update-repair.cmd를 한 번 실행하세요. 그 뒤로는 CAD를 켤 때 자동으로 업데이트됩니다.",
  ].join("\n"),
  files: [
    ...["2010", "2013", "2015", "2025"].map((f) => ({ path: b + `signed/kkumple-2.0.9-${f}.zip`, asset: `kkumple-2.0.9-${f}.zip` })),
    { path: b + "kkumple-update-repair.cmd", asset: "kkumple-update-repair.cmd" },
  ],
});
console.log(r2);
