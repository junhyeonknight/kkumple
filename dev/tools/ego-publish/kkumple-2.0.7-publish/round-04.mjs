const task = await taskSpace(1);
const page = task.page("p1");
await page.click("@61", { label: "confirm new tag" });
await page.waitForTimeout(800);
await page.fill('input[name="release[name]"]', "꿈플 자동 업데이트 2.0.7");
const body = [
  "기존 CAD 명령 별칭(L, CO, TR 등)을 바로 입력하면 '알 수 없는 명령'이 나오고 2~3초 뒤에야 인식되던 문제를 고쳤습니다. 사용자 Support 폴더가 지원 파일 경로에서 빠져 acad.pgp를 읽지 못하던 경우, CAD를 시작할 때 자동으로 경로를 복구하고 명령 별칭을 다시 불러옵니다. 경로가 정상인 PC에서는 아무것도 바꾸지 않습니다.",
  "",
  "함께 포함: 꿈플 관리 화면에 설치 도우미 버전과 설치된 꿈플 버전을 각각 표시하고, 자동 업데이트가 끝나면 패치노트를 보여 줍니다.",
].join("\n");
await page.fill('textarea[name="release[body]"]', body);
const dir = "E:/꿈플-원격스위치/auto-update/releases/2.0.7/signed/";
await page.setInputFiles("#releases-upload", ["2010", "2013", "2015", "2025"].map((f) => dir + "kkumple-2.0.7-" + f + ".zip"));
await page.waitForFunction(() => {
  const names = [...document.querySelectorAll('input[name="release[asset_ids][]"], .js-releases-field input, [data-filename], .js-upload-list li, .release-asset')]
    .map((x) => x.value || x.dataset?.filename || x.textContent);
  const txt = document.body.innerText;
  return ["2010", "2013", "2015", "2025"].every((f) => txt.includes("kkumple-2.0.7-" + f + ".zip")) && !/Uploading|업로드 중/i.test(txt);
}, undefined, { timeout: 120000 });
await page.waitForTimeout(3000);
const state = await page.evaluate(() => ({
  tagButton: document.querySelector('button[aria-label^="Tag:"]')?.innerText.replace(/\s+/g, " "),
  title: document.querySelector('input[name="release[name]"]')?.value,
  bodyLen: document.querySelector('textarea[name="release[body]"]')?.value.length,
  assets: [...document.querySelectorAll('input[name*="asset"]')].map((i) => i.name + "=" + i.value).slice(0, 12),
  uploadText: [...document.body.innerText.matchAll(/kkumple-2\.0\.7-\d+\.zip[^\n]*/g)].map((m) => m[0]),
  errors: [...document.querySelectorAll('.flash-error, .is-bad-file, .is-failed, [class*=error]')].map((e) => e.innerText.trim()).filter(Boolean).slice(0, 5),
  latest: document.querySelector('input[name="release[make_latest]"]:checked, #release_make_latest:checked')?.value ?? null,
}));
console.log(state);
const path = await page.screenshot({ path: "C:/Users/은준현/ego/kkumple-2.0.7-publish/before-publish.png" });
console.log({ screenshot: path });
