export async function uploadDir(page, base, rel, message) {
  const fs = await import("node:fs/promises");
  const names = (await fs.readdir(base + rel, { withFileTypes: true })).filter((d) => d.isFile()).map((d) => d.name);
  const urlPath = ["dev", ...rel.split("/").filter(Boolean)].map(encodeURIComponent).join("/");
  await page.goto("https://github.com/junhyeonknight/kkumple/upload/main/" + urlPath, { timeout: 60000 });
  await page.waitForSelector("#upload-manifest-files-input", { state: "attached", timeout: 30000 });
  await page.setInputFiles("#upload-manifest-files-input", names.map((n) => base + rel + n));
  let st;
  for (let i = 0; i < 60; i++) {
    await page.waitForTimeout(1500);
    st = await page.evaluate(() => ({
      done: document.querySelectorAll(".js-manifest-file-entry:not(.is-hidden), .js-upload-manifest-file:not(.is-hidden)").length,
      busy: !!document.querySelector(".is-uploading"),
      disabled: [...document.querySelectorAll("button")].find((b) => b.innerText.trim() === "Commit changes")?.disabled,
    }));
    if (!st.busy && st.disabled === false && i >= 2) break;
  }
  await page.evaluate(() => document.querySelector("#commit-summary-input").scrollIntoView({ block: "center" }));
  await page.waitForTimeout(500);
  await page.fill("#commit-summary-input", message);
  const pos = await page.evaluate(() => {
    const b = [...document.querySelectorAll("button")].find((x) => x.innerText.trim() === "Commit changes");
    b.scrollIntoView({ block: "center" });
    const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
  });
  await page.mouse.click(pos.x, pos.y, { label: "commit uploaded files to main" });
  await page.waitForURL((u) => !String(u).includes("/upload"), { timeout: 90000 });
  await page.waitForTimeout(1000);
  return { rel, files: names.length, state: st, url: await page.url() };
}
