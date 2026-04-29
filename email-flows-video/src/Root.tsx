import "./index.css";
import type { FC } from "react";
import { Composition } from "remotion";
import { EmailFlows } from "./Composition";

export const RemotionRoot: FC = () => {
  return (
    <>
      <Composition
        id="EmailFlows"
        component={EmailFlows}
        durationInFrames={2880}
        fps={30}
        width={1920}
        height={1080}
      />
    </>
  );
};
