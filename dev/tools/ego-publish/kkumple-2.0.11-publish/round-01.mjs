const { publishRelease } = await import("file:///C:/Users/은준현/ego/kkumple-2.0.8-publish/release-lib.mjs");
const task = await taskSpace("kkumple 2.0.11 publish");
const page = task.page("p1");
const b = "E:/꿈플-원격스위치/auto-update/releases/2.0.11/";
const r = await publishRelease(page, {
  tag: "bundle-v2.0.11",
  title: "꿈플 자동 업데이트 2.0.11",
  label: "latest",
  body: [
    "AutoCAD 2015를 켤 때 \"파일 로드 - 보안 경고\"(Kkumple.MplForms.dll)가 뜨던 문제를 고쳤습니다. 복사본을 이미 신뢰된 꿈플 폴더에 두어 경고 없이 불러옵니다.",
    "",
    "CAD를 켜면 자동으로 업데이트됩니다.",
  ].join("\n"),
  files: ["2010", "2013", "2015", "2025"].map((f) => ({ path: b + `signed/kkumple-2.0.11-${f}.zip`, asset: `kkumple-2.0.11-${f}.zip` })),
});
console.log({ taskSpaceId: task.spaceId, ...r });
