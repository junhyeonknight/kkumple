const task = await taskSpace(1);
const page = task.page("p1");
const info = await page.evaluate(() => {
  const btn = [...document.querySelectorAll('button')].find((b) => b.innerText.trim() === "Publish release" && b.getClientRects().length);
  btn?.scrollIntoView({ block: "center" });
  const r = btn?.getBoundingClientRect();
  const boxes = [...document.querySelectorAll('input[type=checkbox], input[type=radio]')].map((c) => ({ name: c.name, id: c.id, value: c.value, checked: c.checked, label: (c.closest('label')?.innerText || document.querySelector(`label[for="${c.id}"]`)?.innerText || "").trim().slice(0, 40) }));
  return { found: !!btn, disabled: btn?.disabled, x: r && r.x + r.width / 2, y: r && r.y + r.height / 2, boxes };
});
console.log(info);
