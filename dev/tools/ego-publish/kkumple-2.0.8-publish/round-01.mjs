const { publishRelease } = await import("file:///C:/Users/은준현/ego/kkumple-2.0.8-publish/release-lib.mjs");
const task = await taskSpace("kkumple 2.0.8 and installer 2.2.6 publish");
const page = task.page("p1");
const inst = "E:/꿈플-원격스위치/installer/release/2.2.6/";
const r1 = await publishRelease(page, {
  tag: "installer-v2.2.6",
  title: "꿈플 설치 도우미 2.2.6",
  label: "none",
  body: [
    "꿈플을 설치한 뒤에는 CAD를 켤 때 받아 둔 설치 도우미 파일도 백그라운드에서 최신으로 바뀝니다. CAD 시작은 기다리지 않으며, 설치 도우미가 열려 있으면 다음 CAD 시작 때 바꿉니다.",
    "설치 도우미를 직접 켜도 먼저 새 버전을 확인해 자동으로 바꾼 뒤 다시 열립니다(2.2.5부터).",
    "",
    "처음 설치할 때는 kkumple-install-2.2.6.zip을 받아 압축을 풀고 꿈플-설치.exe를 실행하세요. kkumple-setup-2.2.6.exe는 자동 업데이트에 쓰는 파일입니다.",
  ].join("\n"),
  files: [
    { path: inst + "kkumple-install-2.2.6.zip", asset: "kkumple-install-2.2.6.zip" },
    { path: inst + "kkumple-setup-2.2.6.exe", asset: "kkumple-setup-2.2.6.exe" },
  ],
});
console.log({ taskSpaceId: task.spaceId, ...r1 });
const b = "E:/꿈플-원격스위치/auto-update/releases/2.0.8/signed/";
const r2 = await publishRelease(page, {
  tag: "bundle-v2.0.8",
  title: "꿈플 자동 업데이트 2.0.8",
  label: "latest",
  body: [
    "CAD를 켤 때 사용자가 받아 둔 꿈플 설치 도우미도 백그라운드에서 최신으로 바뀝니다. CAD 시작은 기다리지 않습니다.",
    "CAD의 '꿈플 관리' 화면이 설치 도우미 2.2.6으로 바뀝니다.",
    "2.0.7의 명령 별칭 수정(L, CO, TR 등 '알 수 없는 명령' 문제)도 포함합니다.",
  ].join("\n"),
  files: ["2010", "2013", "2015", "2025"].map((f) => ({ path: b + `kkumple-2.0.8-${f}.zip`, asset: `kkumple-2.0.8-${f}.zip` })),
});
console.log(r2);
