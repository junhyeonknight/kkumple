const task = await taskSpace("kkumple installer 2.2.5 publish");
const page = task.page("p1");
await page.goto("https://github.com/junhyeonknight/kkumple/releases/new", { timeout: 60000 });
await page.waitForSelector('button[aria-label="Tag: Select tag"]', { timeout: 30000 });
const user = await page.evaluate(() => document.querySelector('meta[name="user-login"]')?.content || null);
if (user !== "junhyeonknight") throw new Error("not signed in as junhyeonknight: " + user);
await page.click('button[aria-label="Tag: Select tag"]', { label: "open tag picker" });
await page.fill('input[placeholder="Search or create a new tag"]', "installer-v2.2.5");
await page.waitForTimeout(600);
await page.click('text="Create new tag"', { label: "create new tag" });
await page.waitForSelector('input[aria-label="Tag name"]', { timeout: 10000 });
const confirm = await page.evaluate(() => {
  const b = [...document.querySelectorAll('[role=dialog] button, dialog button')].find((x) => x.innerText.trim() === "Create" && x.getClientRects().length);
  const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
});
await page.mouse.click(confirm.x, confirm.y, { label: "confirm new tag" });
await page.waitForTimeout(800);
await page.fill('input[name="release[name]"]', "꿈플 설치 도우미 2.2.5");
const body = [
  "설치 도우미를 켜자마자 새 버전을 확인합니다. 새 버전이 있으면 자동으로 받아 바꾼 뒤 다시 열립니다. 인터넷이 안 되거나 '건너뛰기'를 누르면 지금 버전으로 진행합니다. 받은 설치 도우미는 전자서명된 업데이트 정보의 크기와 SHA-256이 맞을 때만 사용합니다.",
  "",
  "처음 설치할 때는 꿈플-배포용-2.2.5.zip을 받아 압축을 풀고 꿈플-설치.exe를 실행하세요. kkumple-setup-2.2.5.exe는 설치 도우미가 자동 업데이트에 사용하는 파일입니다.",
  "",
  "꿈플 본체 2.0.3을 설치하며, CAD를 시작하면 자동 업데이트로 최신 꿈플(2.0.7)이 적용됩니다.",
].join("\n");
await page.fill('textarea[name="release[body]"]', body);
const dir = "E:/꿈플-원격스위치/installer/release/2.2.5/";
await page.setInputFiles("#releases-upload", [dir + "꿈플-배포용-2.2.5.zip", dir + "kkumple-setup-2.2.5.exe"]);
let s;
for (let i = 0; i < 90; i++) {
  await page.waitForTimeout(5000);
  s = await page.evaluate(() => ({
    url: location.href,
    assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((x) => x.value).filter(Boolean),
    busy: !!document.querySelector('.is-uploading, [class*="uploading"]') || /Uploading/i.test(document.body.innerText),
  }));
  if (s.assets.length >= 2 && !s.busy) break;
}
console.log({ taskSpaceId: task.spaceId, ...s });
