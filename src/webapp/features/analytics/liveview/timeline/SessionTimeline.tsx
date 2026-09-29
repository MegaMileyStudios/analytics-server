import { Fragment } from "react";
import { buildSessionTree, TimelineNode } from "./build-session-tree";
import { DelayStep } from "./DelayStep";
import { EventStep } from "./EventStep";
import { FirstEventStep } from "./FirstEventStep";
import { getSessionEndReason } from "./session-end";
import { SessionEndedStep } from "./SessionEndedStep";
import { ViewBlockStep, ViewCloseStep } from "./ViewSteps";
import { WaitingEventsStep } from "./WaitingEventsStep";

type Props = {
  eventsName: string[];
  eventsTimestamp: string[];
  eventsStringProps: string[];
  eventsNumericProps: string[];
  viewsType?: string[];
  viewsName?: string[];
  viewsTimestamp?: string[];
};

export function SessionTimeline(props: Props) {
  const timestamps = [...(props.eventsTimestamp || []), ...(props.viewsTimestamp || [])].map((ts) => new Date(ts).getTime());
  const lastActivityAt = timestamps.length > 0 ? new Date(Math.max(...timestamps)) : undefined;
  const endReason = getSessionEndReason(props.eventsName || [], lastActivityAt, Date.now());

  const tree = buildSessionTree(props, !!endReason);

  return (
    <>
      <TimelineNodes nodes={tree} />
      {endReason ? <SessionEndedStep reason={endReason} /> : <WaitingEventsStep />}
    </>
  );
}

function TimelineNodes(props: { nodes: TimelineNode[] }) {
  return (
    <>
      {props.nodes.map((node, i) => (
        <Fragment key={i}>
          {node.distance > 60000 && <DelayStep seconds={node.distance / 1000} />}
          <TimelineNodeStep node={node} />
        </Fragment>
      ))}
    </>
  );
}

function TimelineNodeStep(props: { node: TimelineNode }) {
  const { node } = props;

  switch (node.kind) {
    case "main":
    case "popup":
      return (
        <ViewBlockStep node={node}>
          <TimelineNodes nodes={node.children} />
        </ViewBlockStep>
      );
    case "close":
      return <ViewCloseStep node={node} />;
    case "event":
      return node.isFirst ? (
        <FirstEventStep
          eventName={node.name}
          timestamp={node.timestamp}
          eventNumericProps={node.numericProps}
          eventStringProps={node.stringProps}
        />
      ) : (
        <EventStep
          eventName={node.name}
          timestamp={node.timestamp}
          eventNumericProps={node.numericProps}
          eventStringProps={node.stringProps}
          distance={node.distance}
        />
      );
  }
}
