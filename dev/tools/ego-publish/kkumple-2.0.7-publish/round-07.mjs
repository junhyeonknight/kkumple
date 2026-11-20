const task = await taskSpace(1);
const page = task.page("p1");
const dir = "E:/꿈플-원격스위치/auto-update/releases/2.0.7/signed/";
const files = ["2010", "2013", "2015", "2025"].map((f) => dir + "kkumple-2.0.7-" + f + ".zip");
const chooserPromise = page.waitForFileChooser({ timeout: 15000 });
await page.click('button[aria-label="Attach binaries by dropping them here or selecting them"]', { label: "attach release binaries" });
const chooser = await chooserPromise;
const result = await chooser.setFiles(files);
console.log({ result });
for (let i = 0; i < 24; i++) {
  await page.waitForTimeout(5000);
  const s = await page.evaluate(() => ({
    zips: [...document.body.innerText.matchAll(/[^\n]*kkumple-2\.0\.7[^\n]*/g)].map((m) => m[0]),
    uploading: /Uploading|업로드/i.test(document.body.innerText),
  }));
  console.log(i, JSON.stringify(s));
  if (s.zips.length >= 4 && !s.uploading) break;
}
