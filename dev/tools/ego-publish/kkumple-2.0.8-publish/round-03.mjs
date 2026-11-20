const task = await taskSpace(3);
await task.finish({ keep: [] });
console.log("finished");
