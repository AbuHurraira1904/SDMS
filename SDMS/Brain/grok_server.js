const express = require("express");
const bodyParser = require("body-parser");
const { puter } = require("@heyputer/puter.js");

const app = express();
app.use(bodyParser.json());

app.post("/chat", async (req, res) => {
    try {
        const { message, model } = req.body;
        const response = await puter.ai.chat(message, {
            model: model || "x-ai/grok-4.3",
        });

        // Ensure Python sees { "content": "...text..." }
        res.json({ content: response.message.content });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.listen(5001, () => {
    console.log("🚀 Grok REST API running on http://127.0.0.1:5001");
});
