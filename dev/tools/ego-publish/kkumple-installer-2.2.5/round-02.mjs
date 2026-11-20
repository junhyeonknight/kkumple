const task = await taskSpace(2);
const page = task.page("p1");
const zipBox = 'input[aria-label="Change this file’s name"][value="-.-2.2.5.zip"]';
await page.fill(zipBox, "kkumple-install-2.2.5.zip");
const body = await page.evaluate(() => document.querySelector('textarea[name="release[body]"]').value);
await page.fill('textarea[name="release[body]"]', body.replace("꿈플-배포용-2.2.5.zip을", "kkumple-install-2.2.5.zip을"));
// Latest 표시는 꿈플 2.0.7 릴리스에 그대로 둔다.
await page.evaluate(() => {
  const none = document.querySelector('input[name="release[release_label_option]"][value="none"]');
  none.click();
});
await page.waitForTimeout(500);
const s = await page.evaluate(() => ({
  assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((x) => x.value + " " + (x.parentElement?.innerText.trim() || "")).filter((x) => x.trim()),
  label: document.querySelector('input[name="release[release_label_option]"]:checked')?.value,
  tag: document.querySelector('input[name="release[tag_name]"]')?.value,
  title: document.querySelector('input[name="release[name]"]')?.value,
  body: document.querySelector('textarea[name="release[body]"]')?.value,
}));
console.log(s);
