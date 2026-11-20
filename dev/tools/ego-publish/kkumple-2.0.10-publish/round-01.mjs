const { publishRelease } = await import("file:///C:/Users/은준현/ego/kkumple-2.0.8-publish/release-lib.mjs");
const task = await taskSpace("kkumple 2.0.10 publish");
const page = task.page("p1");
const b = "E:/꿈플-원격스위치/auto-update/releases/2.0.10/";
const r = await publishRelease(page, {
  tag: "bundle-v2.0.10",
  title: "꿈플 자동 업데이트 2.0.10",
  label: "latest",
  body: [
    "AutoCAD 2015를 켤 때마다 \"파일 로드 - 보안 경고\"(Kkumple.MplForms.dll)가 뜨던 문제를 고쳤습니다. 꿈플이 사용하는 복사본 폴더를 신뢰할 수 있는 위치에 자동으로 등록합니다.",
    "",
    "CAD를 켜면 자동으로 업데이트됩니다. 업데이트가 적용되는 첫 실행에서는 경고가 한 번 더 뜰 수 있으니 '로드'를 누르세요.",
  ].join("\n"),
  files: ["2010", "2013", "2015", "2025"].map((f) => ({ path: b + `signed/kkumple-2.0.10-${f}.zip`, asset: `kkumple-2.0.10-${f}.zip` })),
});
console.log({ taskSpaceId: task.spaceId, ...r });
